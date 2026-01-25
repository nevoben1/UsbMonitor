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
        /// <returns>Dictionary of property name to property value, or null if device not found or error occurs</returns>
        public static Dictionary<string, string> GetDeviceProperties(string devicePath)
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
                System.Diagnostics.Debug.WriteLine($"GetDeviceProperties: Could not extract VID/PID from device path: {devicePath}");
                return null;
            }

            // Run WMI query on a separate MTA thread to avoid COM threading issues
            // WMI requires proper COM apartment threading and can fail when called from STA threads
            Dictionary<string, string> result = null;
            Exception thrownException = null;

            var thread = new Thread(() =>
            {
                try
                {
                    result = GetDevicePropertiesInternal(vendorId, productId);
                }
                catch (Exception ex)
                {
                    thrownException = ex;
                }
            });

            // Set apartment state to MTA (Multi-Threaded Apartment) for COM interop
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();

            // Wait for the thread to complete (with timeout)
            if (!thread.Join(5000)) // 5 second timeout
            {
                System.Diagnostics.Debug.WriteLine("WMI query timed out after 5 seconds");
                return null;
            }

            if (thrownException != null)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in WMI query thread: {thrownException.Message}");
                return null;
            }

            return result;
        }

        /// <summary>
        /// Extracts a value from a USB device path string
        ///
        /// USB device paths look like: \\?\USB#VID_046D&PID_C52B#...
        /// This method finds "VID_" or "PID_" and extracts the hex value following it
        ///
        /// Example: ExtractValue("USB#VID_046D&PID_C52B", "VID_") returns "046D"
        /// </summary>
        /// <param name="path">The full USB device path</param>
        /// <param name="prefix">The prefix to search for (e.g., "VID_" or "PID_")</param>
        /// <returns>The extracted value, or empty string if not found</returns>
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

                    // Find where the value ends (marked by & or #)
                    int endIndex = path.IndexOf('&', startIndex);
                    if (endIndex < 0) endIndex = path.IndexOf('#', startIndex);
                    if (endIndex < 0) endIndex = path.Length;

                    // Extract and return the value
                    return path.Substring(startIndex, endIndex - startIndex);
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
        private static Dictionary<string, string> GetDevicePropertiesInternal(string vendorId, string productId)
        {
            try
            {
                // Construct WMI query to find device by VID/PID
                // Win32_PnPEntity represents all Plug and Play devices
                // DeviceID typically looks like: USB\VID_046D&PID_C52B\...
                string vidPidPattern = $"VID_{vendorId.ToUpper()}&PID_{productId.ToUpper()}";
                string query = $"SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE '%{vidPidPattern}%'";

                System.Diagnostics.Debug.WriteLine($"WMI Query: {query}");

                using (var searcher = new ManagementObjectSearcher(query))
                {
                    var results = searcher.Get();

                    if (results.Count == 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"No WMI device found for VID:{vendorId} PID:{productId}");
                        return null;
                    }

                    // Get the first matching device
                    // Note: There might be multiple matches if the same device model is connected multiple times
                    // For now, we take the first one. Future enhancement: match by device instance path
                    var device = results.Cast<ManagementObject>().FirstOrDefault();

                    if (device == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"No device object returned from WMI query");
                        return null;
                    }

                    // Extract properties into a dictionary with friendly names
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

                    System.Diagnostics.Debug.WriteLine($"Retrieved {properties.Count} properties for VID:{vendorId} PID:{productId}");

                    // Log all retrieved properties for debugging
                    foreach (var prop in properties)
                    {
                        System.Diagnostics.Debug.WriteLine($"  {prop.Key} = {prop.Value}");
                    }

                    return properties;
                }
            }
            catch (ManagementException ex)
            {
                // WMI-specific exceptions (query errors, access denied, etc.)
                System.Diagnostics.Debug.WriteLine($"WMI ManagementException while retrieving properties for VID:{vendorId} PID:{productId}: {ex.Message}");
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                // Access denied (requires admin rights for some WMI queries)
                System.Diagnostics.Debug.WriteLine($"Access denied while retrieving device properties: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                // Other exceptions
                System.Diagnostics.Debug.WriteLine($"Error retrieving device properties for VID:{vendorId} PID:{productId}: {ex.Message}");
                return null;
            }
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
                    if (value is string[] arrayValue)
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
                System.Diagnostics.Debug.WriteLine($"Error reading property {wmiPropertyName}: {ex.Message}");
            }
        }
    }
}
