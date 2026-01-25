using System;
using System.Text.RegularExpressions;

namespace UsbMonitorLib
{
    /// <summary>
    /// Defines a validation rule for a USB device property
    ///
    /// USAGE EXAMPLES:
    ///
    /// // Single value validation
    /// new PropertyValidator
    /// {
    ///     PropertyName = "FriendlyName",
    ///     ExpectedValues = new[] { "Logitech" },
    ///     Method = ValidationMethod.Contains
    /// }
    ///
    /// // Multiple possible values (ANY match wins)
    /// new PropertyValidator
    /// {
    ///     PropertyName = "Manufacturer",
    ///     ExpectedValues = new[] { "Logitech", "Logitech Inc.", "Logitech, Inc." },
    ///     Method = ValidationMethod.Contains
    /// }
    ///
    /// // Exclude multiple values (must NOT match ANY)
    /// new PropertyValidator
    /// {
    ///     PropertyName = "Manufacturer",
    ///     ExpectedValues = new[] { "Unknown", "(Unknown)", "(Standard)" },
    ///     Method = ValidationMethod.NotEquals
    /// }
    ///
    /// // Multiple regex patterns
    /// new PropertyValidator
    /// {
    ///     PropertyName = "DeviceID",
    ///     ExpectedValues = new[] { @"USB\\VID_046D", @"USB\\VID_045E" },
    ///     Method = ValidationMethod.Regex
    /// }
    /// </summary>
    public class PropertyValidator
    {
        /// <summary>
        /// The name of the device property to validate
        ///
        /// Common property names:
        /// - "Name" or "FriendlyName" - User-friendly device name
        /// - "Manufacturer" - Device manufacturer
        /// - "Description" - Device description
        /// - "DeviceID" - Hardware device identifier
        /// - "Status" - Device status (e.g., "OK", "Error")
        /// - "Service" - Associated Windows service name
        /// </summary>
        public string PropertyName { get; set; }

        /// <summary>
        /// The expected values to validate against
        /// For Regex method, these should be valid regular expression patterns
        ///
        /// MULTIPLE VALUES:
        /// You can specify multiple possible values as a string array
        /// Example: new[] { "Logitech", "Logitech Inc.", "Logitech, Inc." }
        ///
        /// For positive validation methods (Equals, Contains, StartsWith, EndsWith, Regex):
        ///   - Returns true if ANY value matches
        /// For negative validation methods (NotEquals, NotContains):
        ///   - Returns true only if NONE of the values match
        /// </summary>
        public string[] ExpectedValues { get; set; }

        /// <summary>
        /// The validation method to use when comparing the actual value to the expected value
        /// Default: ValidationMethod.Equals
        /// </summary>
        public ValidationMethod Method { get; set; } = ValidationMethod.Equals;

        /// <summary>
        /// Validates an actual property value against this validator's rules
        /// Supports multiple expected values in the ExpectedValues array
        /// </summary>
        /// <param name="actualValue">The actual property value from the device</param>
        /// <returns>True if validation passes, false otherwise</returns>
        public bool Validate(string actualValue)
        {
            // Null/empty handling
            if (actualValue == null)
                actualValue = string.Empty;

            if (ExpectedValues == null || ExpectedValues.Length == 0)
                return false;

            string[] expectedValues = ExpectedValues;

            // Perform validation based on method
            switch (Method)
            {
                case ValidationMethod.Equals:
                    // Return true if actual value equals ANY of the expected values (case-insensitive)
                    foreach (string expected in expectedValues)
                    {
                        if (actualValue.Equals(expected, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    return false;

                case ValidationMethod.EqualsCaseSensitive:
                    // Return true if actual value equals ANY of the expected values (case-sensitive)
                    foreach (string expected in expectedValues)
                    {
                        if (actualValue.Equals(expected, StringComparison.Ordinal))
                            return true;
                    }
                    return false;

                case ValidationMethod.Contains:
                    // Return true if actual value contains ANY of the expected values
                    foreach (string expected in expectedValues)
                    {
                        if (actualValue.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                    return false;

                case ValidationMethod.StartsWith:
                    // Return true if actual value starts with ANY of the expected values
                    foreach (string expected in expectedValues)
                    {
                        if (actualValue.StartsWith(expected, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    return false;

                case ValidationMethod.EndsWith:
                    // Return true if actual value ends with ANY of the expected values
                    foreach (string expected in expectedValues)
                    {
                        if (actualValue.EndsWith(expected, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    return false;

                case ValidationMethod.NotEquals:
                    // Return true only if actual value does NOT equal ANY of the expected values
                    foreach (string expected in expectedValues)
                    {
                        if (actualValue.Equals(expected, StringComparison.OrdinalIgnoreCase))
                            return false; // Found a match, validation fails
                    }
                    return true; // No matches found, validation passes

                case ValidationMethod.NotContains:
                    // Return true only if actual value does NOT contain ANY of the expected values
                    foreach (string expected in expectedValues)
                    {
                        if (actualValue.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0)
                            return false; // Found a match, validation fails
                    }
                    return true; // No matches found, validation passes

                case ValidationMethod.Regex:
                    // Return true if actual value matches ANY of the regex patterns
                    foreach (string pattern in expectedValues)
                    {
                        try
                        {
                            if (System.Text.RegularExpressions.Regex.IsMatch(actualValue, pattern))
                                return true;
                        }
                        catch (ArgumentException ex)
                        {
                            // Invalid regex pattern, log and continue to next pattern
                            System.Diagnostics.Debug.WriteLine(string.Format("Invalid regex pattern '{0}': {1}", pattern, ex.Message));
                        }
                    }
                    return false;

                default:
                    // Unknown validation method
                    System.Diagnostics.Debug.WriteLine(string.Format("Unknown validation method: {0}", Method));
                    return false;
            }
        }

        /// <summary>
        /// Returns a string representation of this validator for debugging
        /// </summary>
        public override string ToString()
        {
            string values = ExpectedValues != null && ExpectedValues.Length > 0
                ? string.Join(", ", ExpectedValues)
                : "(empty)";
            return string.Format("{0} {1} [{2}]", PropertyName, Method, values);
        }
    }
}
