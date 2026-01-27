using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;

namespace UsbMonitorLib
{
    /// <summary>
    /// Retrieves device properties from Windows using WMI (Windows Management Instrumentation)
    ///
    /// This class uses the Win32_PnPEntity WMI class to query USB device properties.
    /// WMI provides a high-level interface to device information without requiring P/Invoke.
    ///
    /// PERFORMANCE NOTE:
    /// WMI queries typically take 50-200ms. This is acceptable for USB device connect/disconnect
    /// events which are infrequent. The query only happens when VID/PID matches, so the performance
    /// impact is minimal in practice.
    /// </summary>
    public static class DevicePropertyRetriever
    {
        /// <summary>
        /// Retrieves properties for a USB device using WMI
        ///
        /// IMPORTANT: WMI uses COM and has threading requirements.
        /// This method runs the WMI query on a separate MTA thread to avoid COM threading issues
        /// when called from STA threads (like the Windows message loop).
        /// </summary>
        /// <param name="devicePath">The full Windows device path (e.g., "\\?\USB#VID_046D&PID_C52B#...")</param>
        /// <param name="propertyValidators">Optional validators used to select the correct WMI result when multiple devices share the same VID/PID</param>
        /// <returns>Dictionary of property name to property value, or null if device not found or error occurs</returns>
        public static Dictionary<string, string> GetDeviceProperties(string devicePath, PropertyValidator[] propertyValidators = null)
        {
            if (string.IsNullOrWhiteSpace(devicePath))
            {
                System.Diagnostics.Debug.WriteLine("GetDeviceProperties: DevicePath is null/empty");
                return null;
            }

            // Extract VID and PID from device path
            string vendorId = ExtractValue(devicePath, "VID_");
            string productId = ExtractValue(devicePath, "PID_");

            if (string.IsNullOrWhiteSpace(vendorId) || string.IsNullOrWhiteSpace(productId))
            {
                System.Diagnostics.Debug.WriteLine(string.Format("GetDeviceProperties: Could not extract VID/PID from device path: {0}", devicePath));
                return null;
            }

            // Run WMI query on a separate MTA thread to avoid COM threading issues
            // WMI requires proper COM apartment threading and can fail when called from STA threads
            // Using ManualResetEvent for proper synchronization and memory barriers
            Dictionary<string, string> result = null;
            Exception thrownException = null;
            var completedEvent = new ManualResetEvent(false);

            var thread = new Thread(() =>
            {
                try
                {
                    result = GetDevicePropertiesInternal(vendorId, productId, propertyValidators);
                }
                catch (Exception ex)
                {
                    thrownException = ex;
                }
                finally
                {
                    // Signal that the thread has completed
                    completedEvent.Set();
                }
            });

            // Set apartment state to MTA (Multi-Threaded Apartment) for COM interop
            thread.SetApartmentState(ApartmentState.MTA);

            System.Diagnostics.Debug.WriteLine(string.Format("Starting WMI query thread for VID:{0} PID:{1}", vendorId, productId));
            var startTime = System.Diagnostics.Stopwatch.StartNew();
            thread.Start();

            // Wait for the thread to complete (with timeout)
            // WaitOne provides proper memory barriers to ensure visibility of writes from the worker thread
            if (!completedEvent.WaitOne(5000)) // 5 second timeout
            {
                System.Diagnostics.Debug.WriteLine(string.Format("WMI query timed out after {0}ms", startTime.ElapsedMilliseconds));
                completedEvent.Dispose();
                return null;
            }

            startTime.Stop();
            System.Diagnostics.Debug.WriteLine(string.Format("WMI query completed in {0}ms", startTime.ElapsedMilliseconds));

            completedEvent.Dispose();

            if (thrownException != null)
            {
                System.Diagnostics.Debug.WriteLine(string.Format("Exception in WMI query thread: {0}", thrownException.Message));
                return null;
            }

            return result;
        }

        /// <summary>
        /// Extracts a value from a USB device path string
        ///
        /// USB device paths look like: \\?\USB#VID_046D&PID_C52B#...
        /// This method finds "VID_" or "PID_" and extracts the 4-character hex value following it
        ///
        /// Example: ExtractValue("USB#VID_046D&PID_C52B", "VID_") returns "046D"
        /// </summary>
        /// <param name="path">The full USB device path</param>
        /// <param name="prefix">The prefix to search for (e.g., "VID_" or "PID_")</param>
        /// <returns>The extracted 4-character value, or empty string if not found</returns>
        private static string ExtractValue(string path, string prefix)
        {
            try
            {
                // Find where the prefix starts in the path
                int startIndex = path.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                if (startIndex >= 0)
                {
                    // Move past the prefix to the actual value
                    startIndex += prefix.Length;

                    // VID and PID are always exactly 4 hexadecimal characters
                    // Extract exactly 4 characters after the prefix
                    if (startIndex + 4 <= path.Length)
                    {
                        return path.Substring(startIndex, 4);
                    }
                }
            }
            catch
            {
                // If anything goes wrong, just return empty
            }
            return string.Empty;
        }

        /// <summary>
        /// Internal method that performs the actual WMI query
        /// Must be called from an MTA thread
        /// </summary>
        private static Dictionary<string, string> GetDevicePropertiesInternal(string vendorId, string productId, PropertyValidator[] propertyValidators)
        {
            try
            {
                // Construct WMI query to find device by VID/PID
                // Win32_PnPEntity represents all Plug and Play devices
                // DeviceID typically looks like: USB\VID_046D&PID_C52B\...
                string vidPidPattern = string.Format("VID_{0}&PID_{1}", vendorId.ToUpper(), productId.ToUpper());
                string query = string.Format("SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE '%{0}%'", vidPidPattern);

                System.Diagnostics.Debug.WriteLine(string.Format("WMI Query: {0}", query));

                using (var searcher = new ManagementObjectSearcher(query))
                {
                    var results = searcher.Get();

                    if (results.Count == 0)
                    {
                        System.Diagnostics.Debug.WriteLine(string.Format("No WMI device found for VID:{0} PID:{1}", vendorId, productId));
                        return null;
                    }

                    System.Diagnostics.Debug.WriteLine(string.Format("Found {0} WMI result(s) for VID:{1} PID:{2}", results.Count, vendorId, productId));

                    // Iterate all matching WMI results and return the one that matches all property validators.
                    // A single VID/PID can produce multiple PnP entities (e.g. composite USB devices),
                    // each with different property values. We need to find the right one.
                    foreach (var device in results.Cast<ManagementObject>())
                    {
                        // Extract properties into a dictionary with friendly names
                        var properties = ExtractProperties(device);

                        System.Diagnostics.Debug.WriteLine(string.Format("Evaluating WMI result: DeviceID={0}", properties.ContainsKey("DeviceID") ? properties["DeviceID"] : "(unknown)"));

                        // Log all retrieved properties for debugging
                        foreach (var prop in properties)
                        {
                            System.Diagnostics.Debug.WriteLine(string.Format("  {0} = {1}", prop.Key, prop.Value));
                        }

                        // If no validators are provided, return the first result (backwards compatible)
                        if (propertyValidators == null || propertyValidators.Length == 0)
                        {
                            System.Diagnostics.Debug.WriteLine(string.Format("No property validators specified, returning first result for VID:{0} PID:{1}", vendorId, productId));
                            return properties;
                        }

                        // Check if ALL validators pass for this device
                        bool allValidatorsPass = true;
                        foreach (var validator in propertyValidators)
                        {
                            string actualValue;
                            if (!properties.TryGetValue(validator.PropertyName, out actualValue))
                            {
                                System.Diagnostics.Debug.WriteLine(string.Format("  Property '{0}' not found, skipping this result", validator.PropertyName));
                                allValidatorsPass = false;
                                break;
                            }

                            if (!validator.Validate(actualValue))
                            {
                                System.Diagnostics.Debug.WriteLine(string.Format("  Validator failed: {0} (actual: '{1}'), skipping this result", validator, actualValue));
                                allValidatorsPass = false;
                                break;
                            }

                            System.Diagnostics.Debug.WriteLine(string.Format("  Validator passed: {0} (actual: '{1}')", validator, actualValue));
                        }

                        if (allValidatorsPass)
                        {
                            System.Diagnostics.Debug.WriteLine(string.Format("All {0} validator(s) passed for this WMI result", propertyValidators.Length));
                            return properties;
                        }
                    }

                    // No WMI result matched all validators
                    System.Diagnostics.Debug.WriteLine(string.Format("No WMI result matched all property validators for VID:{0} PID:{1}", vendorId, productId));
                    return null;
                }
            }
            catch (ManagementException ex)
            {
                // WMI-specific exceptions (query errors, access denied, etc.)
                System.Diagnostics.Debug.WriteLine(string.Format("WMI ManagementException while retrieving properties for VID:{0} PID:{1}: {2}", vendorId, productId, ex.Message));
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                // Access denied (requires admin rights for some WMI queries)
                System.Diagnostics.Debug.WriteLine(string.Format("Access denied while retrieving device properties: {0}", ex.Message));
                return null;
            }
            catch (Exception ex)
            {
                // Other exceptions
                System.Diagnostics.Debug.WriteLine(string.Format("Error retrieving device properties for VID:{0} PID:{1}: {2}", vendorId, productId, ex.Message));
                return null;
            }
        }

        /// <summary>
        /// Extracts all known properties from a WMI ManagementObject into a dictionary
        /// </summary>
        private static Dictionary<string, string> ExtractProperties(ManagementObject device)
        {
            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Map WMI property names to friendly names
            // The key is the friendly name (what users will use in PropertyValidator)
            // The value is retrieved from the WMI property
            AddProperty(properties, device, "Name", "Name");
            AddProperty(properties, device, "FriendlyName", "Name"); // Alias for Name
            AddProperty(properties, device, "Description", "Description");
            AddProperty(properties, device, "Manufacturer", "Manufacturer");
            AddProperty(properties, device, "DeviceID", "DeviceID");
            AddProperty(properties, device, "PNPDeviceID", "PNPDeviceID");
            AddProperty(properties, device, "Status", "Status");
            AddProperty(properties, device, "Service", "Service");
            AddProperty(properties, device, "Caption", "Caption");
            AddProperty(properties, device, "ClassGuid", "ClassGuid");
            AddProperty(properties, device, "ConfigManagerErrorCode", "ConfigManagerErrorCode");
            AddProperty(properties, device, "ConfigManagerUserConfig", "ConfigManagerUserConfig");
            AddProperty(properties, device, "CreationClassName", "CreationClassName");
            AddProperty(properties, device, "ErrorCleared", "ErrorCleared");
            AddProperty(properties, device, "ErrorDescription", "ErrorDescription");
            AddProperty(properties, device, "HardwareID", "HardwareID");
            AddProperty(properties, device, "InstallDate", "InstallDate");
            AddProperty(properties, device, "LastErrorCode", "LastErrorCode");
            AddProperty(properties, device, "PNPClass", "PNPClass");
            AddProperty(properties, device, "PowerManagementCapabilities", "PowerManagementCapabilities");
            AddProperty(properties, device, "PowerManagementSupported", "PowerManagementSupported");
            AddProperty(properties, device, "StatusInfo", "StatusInfo");
            AddProperty(properties, device, "SystemCreationClassName", "SystemCreationClassName");
            AddProperty(properties, device, "SystemName", "SystemName");

            return properties;
        }

        /// <summary>
        /// Helper method to safely add a property from a WMI object to the dictionary
        /// </summary>
        /// <param name="dictionary">The dictionary to add to</param>
        /// <param name="device">The WMI device object</param>
        /// <param name="friendlyName">The friendly name to use as the dictionary key</param>
        /// <param name="wmiPropertyName">The actual WMI property name</param>
        private static void AddProperty(Dictionary<string, string> dictionary, ManagementObject device, string friendlyName, string wmiPropertyName)
        {
            try
            {
                var value = device[wmiPropertyName];
                if (value != null)
                {
                    // Handle array properties (like HardwareID)
                    var arrayValue = value as string[];
                    if (arrayValue != null)
                    {
                        dictionary[friendlyName] = string.Join("; ", arrayValue);
                    }
                    else
                    {
                        dictionary[friendlyName] = value.ToString();
                    }
                }
                else
                {
                    // Property exists but is null
                    dictionary[friendlyName] = string.Empty;
                }
            }
            catch (ManagementException)
            {
                // Property doesn't exist or can't be accessed - skip it
                // This is normal, not all devices have all properties
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(string.Format("Error reading property {0}: {1}", wmiPropertyName, ex.Message));
            }
        }
    }
}
