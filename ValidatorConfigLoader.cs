using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace UsbMonitorLib
{
    /// <summary>
    /// Loads device property validators from an XML configuration file
    ///
    /// XML Format:
    /// <DeviceValidators>
    ///   <Device VidPid="VID_046D&PID_C52B">
    ///     <PropertyValidator PropertyName="Manufacturer" ExpectedValue="Logitech" Method="Contains" />
    ///     <PropertyValidator PropertyName="Status" ExpectedValue="OK" Method="Equals" />
    ///   </Device>
    /// </DeviceValidators>
    ///
    /// USAGE:
    /// // Load validators from XML file
    /// var validators = ValidatorConfigLoader.LoadValidators("VID_046D&PID_C52B", "DeviceValidators.xml");
    ///
    /// // Use with registration
    /// monitor.Register("VID_046D&PID_C52B", OnConnect, OnDisconnect, validators);
    /// </summary>
    public static class ValidatorConfigLoader
    {
        // Cache for loaded XML documents to avoid re-parsing
        private static Dictionary<string, XDocument> _xmlCache = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);
        private static object _cacheLock = new object();

        /// <summary>
        /// Loads property validators for a specific VID/PID from an XML configuration file
        /// </summary>
        /// <param name="vidPid">The VID/PID string (e.g., "VID_046D&PID_C52B")</param>
        /// <param name="xmlFilePath">Path to the XML configuration file (relative or absolute)</param>
        /// <returns>Array of PropertyValidator objects, or null if not found or error occurs</returns>
        public static PropertyValidator[] LoadValidators(string vidPid, string xmlFilePath = "DeviceValidators.xml")
        {
            if (string.IsNullOrWhiteSpace(vidPid))
            {
                System.Diagnostics.Debug.WriteLine("LoadValidators: VidPid is null or empty");
                return null;
            }

            if (string.IsNullOrWhiteSpace(xmlFilePath))
            {
                System.Diagnostics.Debug.WriteLine("LoadValidators: xmlFilePath is null or empty");
                return null;
            }

            try
            {
                // Convert to absolute path if relative
                string absolutePath = Path.IsPathRooted(xmlFilePath)
                    ? xmlFilePath
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, xmlFilePath);

                // Check if file exists
                if (!File.Exists(absolutePath))
                {
                    System.Diagnostics.Debug.WriteLine(string.Format("XML configuration file not found: {0}", absolutePath));
                    return null;
                }

                // Load XML document (with caching)
                XDocument doc = LoadXmlDocument(absolutePath);
                if (doc == null)
                {
                    System.Diagnostics.Debug.WriteLine(string.Format("Failed to load XML document: {0}", absolutePath));
                    return null;
                }

                // Find the Device element matching the VidPid (case-insensitive)
                var deviceElement = doc.Root != null
                    ? doc.Root.Elements("Device").FirstOrDefault(d =>
                    {
                        var vidPidAttr = d.Attribute("VidPid");
                        return vidPidAttr != null && string.Equals(vidPidAttr.Value, vidPid, StringComparison.OrdinalIgnoreCase);
                    })
                    : null;

                if (deviceElement == null)
                {
                    System.Diagnostics.Debug.WriteLine(string.Format("No validators found in XML for VidPid: {0}", vidPid));
                    return null;
                }

                // Parse PropertyValidator elements
                var validators = new List<PropertyValidator>();
                foreach (var validatorElement in deviceElement.Elements("PropertyValidator"))
                {
                    try
                    {
                        var propertyNameAttr = validatorElement.Attribute("PropertyName");
                        var expectedValueAttr = validatorElement.Attribute("ExpectedValue");
                        var methodAttr = validatorElement.Attribute("Method");

                        var propertyName = propertyNameAttr != null ? propertyNameAttr.Value : null;
                        var expectedValue = expectedValueAttr != null ? expectedValueAttr.Value : null;
                        var methodStr = methodAttr != null ? methodAttr.Value : null;

                        // Validate required attributes
                        if (string.IsNullOrWhiteSpace(propertyName))
                        {
                            System.Diagnostics.Debug.WriteLine("PropertyValidator missing PropertyName attribute, skipping");
                            continue;
                        }

                        if (expectedValue == null)
                            expectedValue = string.Empty;

                        // Parse validation method (default to Equals if not specified or invalid)
                        ValidationMethod method = ValidationMethod.Equals;
                        if (!string.IsNullOrWhiteSpace(methodStr))
                        {
                            if (!Enum.TryParse(methodStr, true, out method))
                            {
                                System.Diagnostics.Debug.WriteLine(string.Format("Invalid validation method '{0}', defaulting to Equals", methodStr));
                                method = ValidationMethod.Equals;
                            }
                        }

                        // Create validator
                        var validator = new PropertyValidator
                        {
                            PropertyName = propertyName,
                            ExpectedValue = expectedValue,
                            Method = method
                        };

                        validators.Add(validator);
                        System.Diagnostics.Debug.WriteLine(string.Format("Loaded validator from XML: {0}", validator));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(string.Format("Error parsing PropertyValidator element: {0}", ex.Message));
                        // Continue with other validators
                    }
                }

                if (validators.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine(string.Format("Loaded {0} validator(s) from XML for VidPid: {1}", validators.Count, vidPid));
                    return validators.ToArray();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(string.Format("No valid validators found in XML for VidPid: {0}", vidPid));
                    return null;
                }
            }
            catch (System.Xml.XmlException ex)
            {
                System.Diagnostics.Debug.WriteLine(string.Format("XML parsing error: {0}", ex.Message));
                return null;
            }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine(string.Format("IO error reading XML file: {0}", ex.Message));
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(string.Format("Unexpected error loading validators from XML: {0}", ex.Message));
                return null;
            }
        }

        /// <summary>
        /// Loads an XML document from file with caching
        /// </summary>
        /// <param name="filePath">Absolute path to XML file</param>
        /// <returns>XDocument or null if error occurs</returns>
        private static XDocument LoadXmlDocument(string filePath)
        {
            lock (_cacheLock)
            {
                // Check cache first
                XDocument cachedDoc;
                if (_xmlCache.TryGetValue(filePath, out cachedDoc))
                {
                    return cachedDoc;
                }

                // Load from file
                try
                {
                    var doc = XDocument.Load(filePath);
                    _xmlCache[filePath] = doc;
                    return doc;
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Clears the XML document cache
        /// Call this if you modify the XML file and want to reload it
        /// </summary>
        public static void ClearCache()
        {
            lock (_cacheLock)
            {
                _xmlCache.Clear();
                System.Diagnostics.Debug.WriteLine("ValidatorConfigLoader cache cleared");
            }
        }

        /// <summary>
        /// Clears the cached XML document for a specific file
        /// </summary>
        /// <param name="xmlFilePath">Path to the XML file to remove from cache</param>
        public static void ClearCache(string xmlFilePath)
        {
            if (string.IsNullOrWhiteSpace(xmlFilePath))
                return;

            string absolutePath = Path.IsPathRooted(xmlFilePath)
                ? xmlFilePath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, xmlFilePath);

            lock (_cacheLock)
            {
                if (_xmlCache.Remove(absolutePath))
                {
                    System.Diagnostics.Debug.WriteLine(string.Format("Cleared cache for: {0}", absolutePath));
                }
            }
        }

        /// <summary>
        /// Gets all VidPid entries defined in the XML configuration file
        /// Useful for diagnostics and configuration validation
        /// </summary>
        /// <param name="xmlFilePath">Path to the XML configuration file</param>
        /// <returns>List of VidPid strings found in the XML, or empty list if error occurs</returns>
        public static List<string> GetAllVidPids(string xmlFilePath = "DeviceValidators.xml")
        {
            var result = new List<string>();

            try
            {
                string absolutePath = Path.IsPathRooted(xmlFilePath)
                    ? xmlFilePath
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, xmlFilePath);

                if (!File.Exists(absolutePath))
                {
                    System.Diagnostics.Debug.WriteLine(string.Format("XML configuration file not found: {0}", absolutePath));
                    return result;
                }

                XDocument doc = LoadXmlDocument(absolutePath);
                if (doc == null)
                    return result;

                if (doc.Root != null)
                {
                    var vidPids = doc.Root.Elements("Device")
                        .Select(d =>
                        {
                            var attr = d.Attribute("VidPid");
                            return attr != null ? attr.Value : null;
                        })
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .ToList();

                    if (vidPids != null)
                    {
                        result.AddRange(vidPids);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(string.Format("Error reading VidPids from XML: {0}", ex.Message));
            }

            return result;
        }
    }
}
