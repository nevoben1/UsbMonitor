using System;

namespace UsbMonitorLib
{
    /// <summary>
    /// Defines the method used to validate a device property value
    /// </summary>
    public enum ValidationMethod
    {
        /// <summary>
        /// Exact match comparison (case-insensitive)
        /// Example: "Logitech" equals "logitech"
        /// </summary>
        Equals,

        /// <summary>
        /// Check if the property value contains the expected value (case-insensitive)
        /// Example: "Logitech Wireless Mouse" contains "Wireless"
        /// </summary>
        Contains,

        /// <summary>
        /// Check if the property value starts with the expected value (case-insensitive)
        /// Example: "Logitech USB Receiver" starts with "Logitech"
        /// </summary>
        StartsWith,

        /// <summary>
        /// Check if the property value ends with the expected value (case-insensitive)
        /// Example: "USB Input Device" ends with "Device"
        /// </summary>
        EndsWith,

        /// <summary>
        /// Regular expression match
        /// Example: "VID_046D" matches pattern "VID_[0-9A-F]{4}"
        /// </summary>
        Regex,

        /// <summary>
        /// Exact match comparison (case-sensitive)
        /// Example: "Logitech" does NOT equal "logitech"
        /// </summary>
        EqualsCaseSensitive,

        /// <summary>
        /// Check if the property value does NOT equal the expected value (case-insensitive)
        /// Example: Manufacturer is not "Unknown"
        /// </summary>
        NotEquals,

        /// <summary>
        /// Check if the property value does NOT contain the expected value (case-insensitive)
        /// Example: Description does not contain "Generic"
        /// </summary>
        NotContains
    }
}
