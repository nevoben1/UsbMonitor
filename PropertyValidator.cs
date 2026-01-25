using System;
using System.Text.RegularExpressions;

namespace UsbMonitorLib
{
    /// <summary>
    /// Defines a validation rule for a USB device property
    ///
    /// USAGE EXAMPLES:
    ///
    /// // Validate that FriendlyName contains "Logitech"
    /// new PropertyValidator
    /// {
    ///     PropertyName = "FriendlyName",
    ///     ExpectedValue = "Logitech",
    ///     Method = ValidationMethod.Contains
    /// }
    ///
    /// // Validate that Manufacturer exactly equals "Microsoft"
    /// new PropertyValidator
    /// {
    ///     PropertyName = "Manufacturer",
    ///     ExpectedValue = "Microsoft",
    ///     Method = ValidationMethod.Equals
    /// }
    ///
    /// // Validate using regex
    /// new PropertyValidator
    /// {
    ///     PropertyName = "DeviceID",
    ///     ExpectedValue = @"USB\\VID_[0-9A-F]{4}",
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
        /// The expected value to validate against
        /// For Regex method, this should be a valid regular expression pattern
        /// </summary>
        public string ExpectedValue { get; set; }

        /// <summary>
        /// The validation method to use when comparing the actual value to the expected value
        /// Default: ValidationMethod.Equals
        /// </summary>
        public ValidationMethod Method { get; set; } = ValidationMethod.Equals;

        /// <summary>
        /// Validates an actual property value against this validator's rules
        /// </summary>
        /// <param name="actualValue">The actual property value from the device</param>
        /// <returns>True if validation passes, false otherwise</returns>
        public bool Validate(string actualValue)
        {
            // Null/empty handling
            if (actualValue == null)
                actualValue = string.Empty;

            if (ExpectedValue == null)
                ExpectedValue = string.Empty;

            // Perform validation based on method
            switch (Method)
            {
                case ValidationMethod.Equals:
                    return actualValue.Equals(ExpectedValue, StringComparison.OrdinalIgnoreCase);

                case ValidationMethod.EqualsCaseSensitive:
                    return actualValue.Equals(ExpectedValue, StringComparison.Ordinal);

                case ValidationMethod.Contains:
                    return actualValue.IndexOf(ExpectedValue, StringComparison.OrdinalIgnoreCase) >= 0;

                case ValidationMethod.StartsWith:
                    return actualValue.StartsWith(ExpectedValue, StringComparison.OrdinalIgnoreCase);

                case ValidationMethod.EndsWith:
                    return actualValue.EndsWith(ExpectedValue, StringComparison.OrdinalIgnoreCase);

                case ValidationMethod.NotEquals:
                    return !actualValue.Equals(ExpectedValue, StringComparison.OrdinalIgnoreCase);

                case ValidationMethod.NotContains:
                    return actualValue.IndexOf(ExpectedValue, StringComparison.OrdinalIgnoreCase) < 0;

                case ValidationMethod.Regex:
                    try
                    {
                        return System.Text.RegularExpressions.Regex.IsMatch(actualValue, ExpectedValue);
                    }
                    catch (ArgumentException ex)
                    {
                        // Invalid regex pattern
                        System.Diagnostics.Debug.WriteLine(string.Format("Invalid regex pattern '{0}': {1}", ExpectedValue, ex.Message));
                        return false;
                    }

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
            return string.Format("{0} {1} '{2}'", PropertyName, Method, ExpectedValue);
        }
    }
}
