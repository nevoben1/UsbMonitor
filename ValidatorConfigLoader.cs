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
                    System.Diagnostics.Debug.WriteLine($"XML configuration file not found: {absolutePath}");
                    return null;
                }

                // Load XML document (with caching)
                XDocument doc = LoadXmlDocument(absolutePath);
                if (doc == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load XML document: {absolutePath}");
                    return null;
                }

                // Find the Device element matching the VidPid (case-insensitive)
                var deviceElement = doc.Root?.Elements("Device")
                    .FirstOrDefault(d =>
                        string.Equals(
                            d.Attribute("VidPid")?.Value,
                            vidPid,
                            StringComparison.OrdinalIgnoreCase));

                if (deviceElement == null)
                {
                    System.Diagnostics.Debug.WriteLine($"No validators found in XML for VidPid: {vidPid}");
                    return null;
                }

                // Parse PropertyValidator elements
                var validators = new List<PropertyValidator>();
                foreach (var validatorElement in deviceElement.Elements("PropertyValidator"))
                {
                    try
                    {
                        var propertyName = validatorElement.Attribute("PropertyName")?.Value;
                        var expectedValue = validatorElement.Attribute("ExpectedValue")?.Value;
                        var methodStr = validatorElement.Attribute("Method")?.Value;

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
                                System.Diagnostics.Debug.WriteLine($"Invalid validation method '{methodStr}', defaulting to Equals");
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
                        System.Diagnostics.Debug.WriteLine($"Loaded validator from XML: {validator}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error parsing PropertyValidator element: {ex.Message}");
                        // Continue with other validators
                    }
                }

                if (validators.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Loaded {validators.Count} validator(s) from XML for VidPid: {vidPid}");
                    return validators.ToArray();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"No valid validators found in XML for VidPid: {vidPid}");
                    return null;
                }
            }
            catch (XmlException ex)
            {
                System.Diagnostics.Debug.WriteLine($"XML parsing error: {ex.Message}");
                return null;
            }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine($"IO error reading XML file: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Unexpected error loading validators from XML: {ex.Message}");
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
                if (_xmlCache.TryGetValue(filePath, out XDocument cachedDoc))
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
                    System.Diagnostics.Debug.WriteLine($"Cleared cache for: {absolutePath}");
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
                    System.Diagnostics.Debug.WriteLine($"XML configuration file not found: {absolutePath}");
                    return result;
                }

                XDocument doc = LoadXmlDocument(absolutePath);
                if (doc == null)
                    return result;

                var vidPids = doc.Root?.Elements("Device")
                    .Select(d => d.Attribute("VidPid")?.Value)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToList();

                if (vidPids != null)
                {
                    result.AddRange(vidPids);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading VidPids from XML: {ex.Message}");
            }

            return result;
        }
    }
}
