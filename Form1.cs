using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using UsbMonitorLib;

namespace WindowsFormsApp1
{
    /// <summary>
    /// Main form demonstrating USB device monitoring with the singleton pattern
    ///
    /// HOW TO USE THE USB MONITOR (SIMPLE!):
    /// 1. Get the singleton instance: UsbDeviceMonitor.Instance
    /// 2. Register with your VID/PID and callbacks - that's it!
    /// 3. The monitor auto-starts on first registration
    /// 4. Handle callbacks on the UI thread (use Invoke if needed)
    /// 5. Unregister when done (optional)
    ///
    /// HOW TO FIND YOUR USB DEVICE VID/PID:
    /// 1. Plug in your USB device
    /// 2. Open Device Manager (Windows key + X, then M)
    /// 3. Find your device in the list
    /// 4. Right-click -> Properties -> Details tab
    /// 5. Select "Hardware Ids" from the dropdown
    /// 6. Look for "USB\VID_XXXX&PID_YYYY" in the value
    /// 7. Use "VID_XXXX&PID_YYYY" as your registration string
    ///
    /// MULTIPLE LISTENERS EXAMPLE:
    /// - Class A registers for VID_046D&PID_C52B (Logitech device)
    /// - Class B registers for VID_0781&PID_5567 (SanDisk flash drive)
    /// - Both share the same UsbDeviceMonitor.Instance
    /// - Each gets callbacks only for their specific device
    /// - No need to coordinate Start() calls - it's automatic!
    /// </summary>
    public partial class Form1 : Form
    {
        // Registration objects for each device we're monitoring
        private UsbDeviceRegistration registration1;
        private UsbDeviceRegistration registration2;
        private UsbDeviceRegistration registration3;
        private UsbDeviceRegistration registration4;

        public Form1()
        {
            InitializeComponent();

            // Get the singleton instance
            var monitor = UsbDeviceMonitor.Instance;

            // Example 1: Basic registration (no property validation - backwards compatible)
            // Register for a Logitech device (VID_046D&PID_C52B)
            // Replace "VID_046D&PID_C52B" with your actual device's VID/PID from Device Manager
            // The monitor will auto-start when you call Register()
            registration1 = monitor.Register(
                "VID_046D&PID_C52B",            // Windows device path format (case insensitive)
                OnLogitechDeviceConnected,      // Called when this specific device connects
                OnLogitechDeviceDisconnected    // Called when this specific device disconnects
            );

            // Example 2: Registration with inline property validators
            // This demonstrates property validation - callbacks only fire if ALL validators pass
            // This registration will only trigger for SanDisk devices where the FriendlyName contains "USB"
            registration2 = monitor.Register(
                "VID_0781&PID_5567",            // SanDisk device pattern
                OnSandiskDeviceConnected,       // Different callbacks for this device
                OnSandiskDeviceDisconnected,
                // Property validators - ALL must pass for callback to be invoked
                new PropertyValidator
                {
                    PropertyName = "FriendlyName",
                    ExpectedValue = "USB",
                    Method = ValidationMethod.Contains
                }
            );

            // Example 3: Multiple inline property validators
            // This demonstrates validating multiple properties at once
            // Callbacks will only fire if BOTH validators pass (strict AND logic)
            registration3 = monitor.Register(
                "VID_046D&PID_C52B",            // Logitech device
                OnSpecificLogitechConnected,
                OnSpecificLogitechDisconnected,
                new PropertyValidator
                {
                    PropertyName = "Manufacturer",
                    ExpectedValue = "Logitech",
                    Method = ValidationMethod.Contains
                },
                new PropertyValidator
                {
                    PropertyName = "Status",
                    ExpectedValue = "OK",
                    Method = ValidationMethod.Equals
                }
            );

            // Example 4: XML-based registration
            // This demonstrates loading property validators from an XML configuration file
            // The validators for VID_046D&PID_C52B are defined in DeviceValidators.xml
            // If no validators are found in the XML, only VID/PID matching is performed
            registration4 = monitor.RegisterFromXml(
                "VID_046D&PID_C52B",            // VID/PID to look up in XML
                OnXmlBasedDeviceConnected,      // Callback for connect
                OnXmlBasedDeviceDisconnected,   // Callback for disconnect
                "DeviceValidators.xml"          // Optional: XML file path (defaults to "DeviceValidators.xml")
            );

            // That's it! No need to call Start() - it happens automatically
            // You'll now get callbacks when these specific devices are connected/disconnected

            System.Diagnostics.Debug.WriteLine("Form1 registered for USB device notifications");
        }

        /// <summary>
        /// Callback for Logitech device connection (basic registration without property validation)
        ///
        /// IMPORTANT: This is called from the monitor's background thread!
        /// Use Invoke to safely update the UI.
        /// </summary>
        private void OnLogitechDeviceConnected(UsbDeviceEventArgs e)
        {
            // Marshal to UI thread if needed
            if (this.InvokeRequired)
            {
                this.Invoke(new UsbDeviceConnectedCallback(OnLogitechDeviceConnected), e);
                return;
            }

            // Now on UI thread - safe to show MessageBox
            MessageBox.Show(string.Format("LOGITECH DEVICE CONNECTED!\nVID: {0}\nPID: {1}\nPath: {2}",
                e.VendorId, e.ProductId, e.DevicePath));
        }

        /// <summary>
        /// Callback for Logitech device disconnection
        /// </summary>
        private void OnLogitechDeviceDisconnected(UsbDeviceEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new UsbDeviceDisconnectedCallback(OnLogitechDeviceDisconnected), e);
                return;
            }

            MessageBox.Show(string.Format("LOGITECH DEVICE DISCONNECTED!\nVID: {0}\nPID: {1}",
                e.VendorId, e.ProductId));
        }

        /// <summary>
        /// Callback for SanDisk device connection (with property validation)
        /// Only called if FriendlyName contains "USB"
        /// </summary>
        private void OnSandiskDeviceConnected(UsbDeviceEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new UsbDeviceConnectedCallback(OnSandiskDeviceConnected), e);
                return;
            }

            // Build message with properties if available
            string message = string.Format("SANDISK DEVICE CONNECTED!\nVID: {0}\nPID: {1}\nPath: {2}",
                e.VendorId, e.ProductId, e.DevicePath);

            // Show some properties if they were retrieved
            if (e.Properties != null && e.Properties.Count > 0)
            {
                message += "\n\nDevice Properties:";
                if (e.Properties.ContainsKey("FriendlyName"))
                    message += "\nFriendly Name: " + e.Properties["FriendlyName"];
                if (e.Properties.ContainsKey("Manufacturer"))
                    message += "\nManufacturer: " + e.Properties["Manufacturer"];
                if (e.Properties.ContainsKey("Status"))
                    message += "\nStatus: " + e.Properties["Status"];
            }

            MessageBox.Show(message);
        }

        /// <summary>
        /// Callback for SanDisk device disconnection
        /// </summary>
        private void OnSandiskDeviceDisconnected(UsbDeviceEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new UsbDeviceDisconnectedCallback(OnSandiskDeviceDisconnected), e);
                return;
            }

            MessageBox.Show(string.Format("SANDISK DEVICE DISCONNECTED!\nVID: {0}\nPID: {1}",
                e.VendorId, e.ProductId));
        }

        /// <summary>
        /// Callback for specific Logitech device connection (with multiple property validators)
        /// Only called if Manufacturer contains "Logitech" AND Status equals "OK"
        /// </summary>
        private void OnSpecificLogitechConnected(UsbDeviceEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new UsbDeviceConnectedCallback(OnSpecificLogitechConnected), e);
                return;
            }

            // Build detailed message with all properties
            string message = string.Format("SPECIFIC LOGITECH DEVICE CONNECTED!\nVID: {0}\nPID: {1}",
                e.VendorId, e.ProductId);

            message += "\n\nThis device passed all property validations:";
            message += "\n- Manufacturer contains 'Logitech'";
            message += "\n- Status equals 'OK'";

            // Show all retrieved properties
            if (e.Properties != null && e.Properties.Count > 0)
            {
                message += "\n\nAll Device Properties:";
                foreach (var prop in e.Properties)
                {
                    // Limit property value length for display
                    string value = prop.Value;
                    if (value != null && value.Length > 50)
                        value = value.Substring(0, 50) + "...";
                    message += string.Format("\n{0}: {1}", prop.Key, value);
                }
            }

            MessageBox.Show(message);
        }

        /// <summary>
        /// Callback for specific Logitech device disconnection
        /// </summary>
        private void OnSpecificLogitechDisconnected(UsbDeviceEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new UsbDeviceDisconnectedCallback(OnSpecificLogitechDisconnected), e);
                return;
            }

            MessageBox.Show(string.Format("SPECIFIC LOGITECH DEVICE DISCONNECTED!\nVID: {0}\nPID: {1}",
                e.VendorId, e.ProductId));
        }

        /// <summary>
        /// Callback for XML-based device connection
        /// Validators are loaded from DeviceValidators.xml based on VID/PID
        /// </summary>
        private void OnXmlBasedDeviceConnected(UsbDeviceEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new UsbDeviceConnectedCallback(OnXmlBasedDeviceConnected), e);
                return;
            }

            // Build detailed message
            string message = string.Format("XML-BASED DEVICE CONNECTED!\nVID: {0}\nPID: {1}",
                e.VendorId, e.ProductId);

            message += "\n\nThis device matched validators from DeviceValidators.xml";

            // Show properties if available
            if (e.Properties != null && e.Properties.Count > 0)
            {
                message += "\n\nValidated Properties:";

                // Show key properties
                if (e.Properties.ContainsKey("Manufacturer"))
                    message += "\nManufacturer: " + e.Properties["Manufacturer"];
                if (e.Properties.ContainsKey("FriendlyName"))
                    message += "\nFriendly Name: " + e.Properties["FriendlyName"];
                if (e.Properties.ContainsKey("Status"))
                    message += "\nStatus: " + e.Properties["Status"];
            }

            MessageBox.Show(message);
        }

        /// <summary>
        /// Callback for XML-based device disconnection
        /// </summary>
        private void OnXmlBasedDeviceDisconnected(UsbDeviceEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new UsbDeviceDisconnectedCallback(OnXmlBasedDeviceDisconnected), e);
                return;
            }

            MessageBox.Show(string.Format("XML-BASED DEVICE DISCONNECTED!\nVID: {0}\nPID: {1}",
                e.VendorId, e.ProductId));
        }

        /// <summary>
        /// Clean up resources when the form is closing
        /// Unregister our listeners (optional but good practice)
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            var monitor = UsbDeviceMonitor.Instance;

            // Unregister our listeners
            if (registration1 != null)
            {
                monitor.Unregister(registration1);
                registration1 = null;
            }

            if (registration2 != null)
            {
                monitor.Unregister(registration2);
                registration2 = null;
            }

            if (registration3 != null)
            {
                monitor.Unregister(registration3);
                registration3 = null;
            }

            if (registration4 != null)
            {
                monitor.Unregister(registration4);
                registration4 = null;
            }

            // Note: We don't call Stop() or Dispose() on the singleton
            // Other parts of the application might still be using it
            // The singleton will clean up when the application exits

            base.OnFormClosing(e);
        }
    }
}
