using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Threading;
using System.Collections.Generic;
using System.Linq;

namespace UsbMonitorLib
{
    /// <summary>
    /// Callback delegate for USB device connection events
    /// </summary>
    /// <param name="e">Event arguments containing device information</param>
    public delegate void UsbDeviceConnectedCallback(UsbDeviceEventArgs e);

    /// <summary>
    /// Callback delegate for USB device disconnection events
    /// </summary>
    /// <param name="e">Event arguments containing device information</param>
    public delegate void UsbDeviceDisconnectedCallback(UsbDeviceEventArgs e);

    /// <summary>
    /// USB Device Monitor - Singleton class that detects USB device connections and disconnections
    ///
    /// HOW IT WORKS:
    /// 1. Single instance (singleton) that can be shared across multiple classes
    /// 2. Auto-starts on first registration (no need to call Start()!)
    /// 3. Creates a dedicated thread with its own Windows message loop
    /// 4. Creates an invisible message-only window on that thread to receive Windows messages
    /// 5. Multiple classes can register with specific VID/PID filters and callbacks
    /// 6. When USB devices are plugged/unplugged, Windows sends messages to our window
    /// 7. The window's WndProc intercepts these messages and invokes matching callbacks
    ///
    /// USAGE (SIMPLE!):
    /// var reg = UsbDeviceMonitor.Instance.Register(
    ///     "VID_046D&PID_C52B",      // VID/PID in Windows device path format
    ///     OnConnected,              // Connect callback
    ///     OnDisconnected            // Disconnect callback
    /// );
    /// // That's it! The monitor auto-starts and you'll get callbacks when your device connects/disconnects
    ///
    /// // Later, when done (optional):
    /// UsbDeviceMonitor.Instance.Unregister(reg);
    ///
    /// HOW TO FIND YOUR DEVICE'S VID/PID:
    /// 1. Open Device Manager in Windows
    /// 2. Find your USB device
    /// 3. Right-click -> Properties -> Details tab
    /// 4. Select "Hardware Ids" from dropdown
    /// 5. Look for "USB\VID_XXXX&PID_YYYY" in the value
    /// 6. Use "VID_XXXX&PID_YYYY" as your filter string
    ///
    /// WHY A SINGLETON?
    /// - Only one message loop/window is needed for all USB monitoring
    /// - Multiple classes can share the same monitor instance
    /// - Reduces resource usage and simplifies lifecycle management
    ///
    /// WHY A SEPARATE THREAD?
    /// - Windows messages require a message pump (Application.Run)
    /// - Creating our own thread ensures we have a dedicated message loop
    /// - This prevents interference with the main UI thread
    ///
    /// WHY A MESSAGE-ONLY WINDOW?
    /// - We need a window handle (HWND) to receive Windows messages
    /// - Message-only windows (HWND_MESSAGE) receive messages but have no UI presence
    /// - They're perfect for background system notifications
    /// </summary>
    public class UsbDeviceMonitor : IDisposable
    {
        // Singleton instance
        private static UsbDeviceMonitor _instance;
        private static readonly object _lock = new object();

        // The invisible window that receives USB device change messages from Windows
        private MessageOnlyWindow messageWindow;

        // Dedicated thread running the Windows message loop for our window
        private Thread messageLoopThread;

        // Synchronization object to ensure window is created before returning from Start()
        private ManualResetEvent windowCreated = new ManualResetEvent(false);

        // List of all registered listeners with their VID/PID filters and callbacks
        private List<UsbDeviceRegistration> registrations = new List<UsbDeviceRegistration>();
        private readonly object registrationsLock = new object();

        // Track if the monitor has been started
        private bool isStarted = false;

        /// <summary>
        /// Gets the singleton instance of the USB Device Monitor
        /// </summary>
        public static UsbDeviceMonitor Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new UsbDeviceMonitor();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Private constructor - use Instance property to get the singleton
        /// </summary>
        private UsbDeviceMonitor()
        {
        }

        /// <summary>
        /// Registers a listener for USB device events with specific VID/PID filter
        ///
        /// USAGE:
        /// var reg = UsbDeviceMonitor.Instance.Register("VID_046D&PID_C52B", OnConnected, OnDisconnected);
        ///
        /// The monitor automatically starts on the first registration, so you don't need to call Start()!
        ///
        /// VID/PID FORMAT:
        /// - Format: "VID_XXXX&PID_YYYY" (e.g., "VID_046D&PID_C52B")
        /// - Matches the Windows device path format
        /// - XXXX and YYYY are 4-digit hexadecimal values
        /// - Case insensitive
        ///
        /// CALLBACKS:
        /// - onConnect: Called when a matching device is connected (can be null)
        /// - onDisconnect: Called when a matching device is disconnected (can be null)
        /// - At least one callback must be provided
        /// </summary>
        /// <param name="vidPid">VID/PID string (e.g., "VID_046D&PID_C52B")</param>
        /// <param name="onConnect">Callback when device connects (optional)</param>
        /// <param name="onDisconnect">Callback when device disconnects (optional)</param>
        /// <returns>Registration object that can be used to unregister</returns>
        public UsbDeviceRegistration Register(
            string vidPid,
            UsbDeviceConnectedCallback onConnect,
            UsbDeviceDisconnectedCallback onDisconnect)
        {
            return Register(vidPid, onConnect, onDisconnect, null);
        }

        /// <summary>
        /// Registers a listener for USB device events with VID/PID filter and property validators loaded from XML
        ///
        /// USAGE:
        /// var reg = UsbDeviceMonitor.Instance.RegisterFromXml(
        ///     "VID_046D&PID_C52B",
        ///     OnConnected,
        ///     OnDisconnected,
        ///     "DeviceValidators.xml"  // Optional, defaults to "DeviceValidators.xml"
        /// );
        ///
        /// XML FORMAT:
        /// <DeviceValidators>
        ///   <Device VidPid="VID_046D&PID_C52B">
        ///     <PropertyValidator PropertyName="Manufacturer" ExpectedValue="Logitech" Method="Contains" />
        ///     <PropertyValidator PropertyName="Status" ExpectedValue="OK" Method="Equals" />
        ///   </Device>
        /// </DeviceValidators>
        ///
        /// PROPERTY VALIDATION:
        /// - Validators are automatically loaded from the XML file based on VID/PID
        /// - If no validators are found in XML, only VID/PID matching is performed
        /// - All property validators must pass for the callback to be invoked (strict AND logic)
        /// - If any property cannot be retrieved, validation fails and callback is NOT invoked
        ///
        /// VID/PID FORMAT:
        /// - Format: "VID_XXXX&PID_YYYY" (e.g., "VID_046D&PID_C52B")
        /// - Matches the Windows device path format
        /// - XXXX and YYYY are 4-digit hexadecimal values
        /// - Case insensitive
        ///
        /// CALLBACKS:
        /// - onConnect: Called when a matching device is connected (can be null)
        /// - onDisconnect: Called when a matching device is disconnected (can be null)
        /// - At least one callback must be provided
        /// </summary>
        /// <param name="vidPid">VID/PID string (e.g., "VID_046D&PID_C52B")</param>
        /// <param name="onConnect">Callback when device connects (optional)</param>
        /// <param name="onDisconnect">Callback when device disconnects (optional)</param>
        /// <param name="xmlFilePath">Path to XML configuration file (default: "DeviceValidators.xml")</param>
        /// <returns>Registration object that can be used to unregister</returns>
        public UsbDeviceRegistration RegisterFromXml(
            string vidPid,
            UsbDeviceConnectedCallback onConnect,
            UsbDeviceDisconnectedCallback onDisconnect,
            string xmlFilePath = "DeviceValidators.xml")
        {
            // Load validators from XML
            PropertyValidator[] validators = ValidatorConfigLoader.LoadValidators(vidPid, xmlFilePath);

            // Register with loaded validators (or null if none found)
            return Register(vidPid, onConnect, onDisconnect, validators);
        }

        /// <summary>
        /// Registers a listener for USB device events with VID/PID filter and property validation
        ///
        /// USAGE:
        /// var reg = UsbDeviceMonitor.Instance.Register(
        ///     "VID_046D&PID_C52B",
        ///     OnConnected,
        ///     OnDisconnected,
        ///     new PropertyValidator
        ///     {
        ///         PropertyName = "FriendlyName",
        ///         ExpectedValue = "Logitech",
        ///         Method = ValidationMethod.Contains
        ///     },
        ///     new PropertyValidator
        ///     {
        ///         PropertyName = "Manufacturer",
        ///         ExpectedValue = "Logitech",
        ///         Method = ValidationMethod.Equals
        ///     }
        /// );
        ///
        /// PROPERTY VALIDATION:
        /// - All property validators must pass for the callback to be invoked (strict AND logic)
        /// - If any property cannot be retrieved, validation fails and callback is NOT invoked
        /// - If no property validators are provided, only VID/PID matching is performed (backwards compatible)
        /// - Property retrieval uses WMI and may add 50-200ms latency on device connect/disconnect
        ///
        /// COMMON PROPERTY NAMES:
        /// - "Name" or "FriendlyName" - User-friendly device name
        /// - "Manufacturer" - Device manufacturer
        /// - "Description" - Device description
        /// - "Status" - Device status (e.g., "OK", "Error")
        /// - "DeviceID" - Hardware device identifier
        /// - "Service" - Associated Windows service name
        ///
        /// VID/PID FORMAT:
        /// - Format: "VID_XXXX&PID_YYYY" (e.g., "VID_046D&PID_C52B")
        /// - Matches the Windows device path format
        /// - XXXX and YYYY are 4-digit hexadecimal values
        /// - Case insensitive
        ///
        /// CALLBACKS:
        /// - onConnect: Called when a matching device is connected (can be null)
        /// - onDisconnect: Called when a matching device is disconnected (can be null)
        /// - At least one callback must be provided
        /// </summary>
        /// <param name="vidPid">VID/PID string (e.g., "VID_046D&PID_C52B")</param>
        /// <param name="onConnect">Callback when device connects (optional)</param>
        /// <param name="onDisconnect">Callback when device disconnects (optional)</param>
        /// <param name="propertyValidators">Optional property validators that must all pass</param>
        /// <returns>Registration object that can be used to unregister</returns>
        public UsbDeviceRegistration Register(
            string vidPid,
            UsbDeviceConnectedCallback onConnect,
            UsbDeviceDisconnectedCallback onDisconnect,
            params PropertyValidator[] propertyValidators)
        {
            if (string.IsNullOrWhiteSpace(vidPid))
                throw new ArgumentException("VID/PID cannot be null or empty", nameof(vidPid));

            if (onConnect == null && onDisconnect == null)
                throw new ArgumentException("At least one callback (onConnect or onDisconnect) must be provided");

            // Validate the format: VID_XXXX&PID_YYYY
            string upperVidPid = vidPid.ToUpper();

            // Check if it contains both VID_ and PID_
            if (!upperVidPid.Contains("VID_") || !upperVidPid.Contains("PID_"))
                throw new ArgumentException("VID/PID must be in format 'VID_XXXX&PID_YYYY' (e.g., 'VID_046D&PID_C52B')", nameof(vidPid));

            // Check if it contains the & separator
            if (!upperVidPid.Contains("&"))
                throw new ArgumentException("VID/PID must contain '&' separator (e.g., 'VID_046D&PID_C52B')", nameof(vidPid));

            // Extract VID and PID values for logging
            string vid = "Unknown";
            string pid = "Unknown";
            try
            {
                int vidIndex = upperVidPid.IndexOf("VID_") + 4;
                int vidEnd = upperVidPid.IndexOf("&", vidIndex);
                if (vidEnd > vidIndex)
                    vid = upperVidPid.Substring(vidIndex, vidEnd - vidIndex);

                int pidIndex = upperVidPid.IndexOf("PID_") + 4;
                int pidEnd = upperVidPid.IndexOfAny(new[] { '&', '#', '\\' }, pidIndex);
                if (pidEnd < 0) pidEnd = upperVidPid.Length;
                pid = upperVidPid.Substring(pidIndex, pidEnd - pidIndex);
            }
            catch
            {
                // If parsing fails, we'll just use "Unknown" for logging
            }

            // Create the registration
            var registration = new UsbDeviceRegistration
            {
                VidPid = vidPid,
                OnConnect = onConnect,
                OnDisconnect = onDisconnect,
                PropertyValidators = propertyValidators
            };

            // Add to the list
            lock (registrationsLock)
            {
                registrations.Add(registration);
            }

            if (propertyValidators != null && propertyValidators.Length > 0)
            {
                System.Diagnostics.Debug.WriteLine($"Registered listener for VID:{vid} PID:{pid} with {propertyValidators.Length} property validator(s)");
                foreach (var validator in propertyValidators)
                {
                    System.Diagnostics.Debug.WriteLine($"  - {validator}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Registered listener for VID:{vid} PID:{pid} (Pattern: {vidPid})");
            }

            // Auto-start the monitor if this is the first registration
            EnsureStarted();

            return registration;
        }

        /// <summary>
        /// Unregisters a previously registered listener
        /// </summary>
        /// <param name="registration">The registration object returned from Register()</param>
        public void Unregister(UsbDeviceRegistration registration)
        {
            if (registration == null)
                return;

            lock (registrationsLock)
            {
                registrations.Remove(registration);
            }

            System.Diagnostics.Debug.WriteLine($"Unregistered listener for {registration.VidPid}");
        }

        /// <summary>
        /// Ensures the monitor is started
        /// Called automatically by Register() - you don't need to call this manually
        /// Thread-safe and idempotent (safe to call multiple times)
        /// </summary>
        private void EnsureStarted()
        {
            // Double-check locking pattern for thread safety
            if (!isStarted)
            {
                lock (_lock)
                {
                    if (!isStarted)
                    {
                        Start();
                        isStarted = true;
                        System.Diagnostics.Debug.WriteLine("UsbDeviceMonitor auto-started on first registration");
                    }
                }
            }
        }

        /// <summary>
        /// Starts monitoring for USB device changes
        /// Creates a new thread with a message loop to receive Windows notifications
        ///
        /// NOTE: This is now private - the monitor auto-starts when you call Register()
        /// </summary>
        private void Start()
        {
            // Prevent starting multiple times
            if (messageLoopThread != null && messageLoopThread.IsAlive)
                return; // Already started

            windowCreated.Reset();

            // Create a new thread to run the message loop
            // This thread will live as long as the monitor is running
            messageLoopThread = new Thread(() =>
            {
                try
                {
                    // Create the message-only window on THIS thread
                    messageWindow = new MessageOnlyWindow();

                    // Subscribe to the window's events
                    messageWindow.DeviceConnected += OnDeviceConnected;
                    messageWindow.DeviceRemoved += OnDeviceRemoved;

                    // Signal that window is created and ready
                    windowCreated.Set();

                    // Start the Windows message pump for this window
                    // This blocks until the window is closed
                    // All Windows messages (including WM_DEVICECHANGE) will be processed here
                    Application.Run(messageWindow);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Error in message loop: " + ex.Message);
                }
            });

            // STA (Single-Threaded Apartment) is required for Windows Forms
            messageLoopThread.SetApartmentState(ApartmentState.STA);

            // Keep thread alive - not a background thread
            // This ensures the message loop continues running
            messageLoopThread.IsBackground = false;

            // Start the thread
            messageLoopThread.Start();

            // Wait for window to be created (with 5 second timeout)
            // This ensures the monitor is fully initialized before Start() returns
            windowCreated.WaitOne(5000);
        }

        /// <summary>
        /// Called when a USB device is connected
        /// Iterates through all registrations and invokes callbacks for matching VID/PID and properties
        /// </summary>
        private void OnDeviceConnected(object sender, UsbDeviceEventArgs e)
        {
            List<UsbDeviceRegistration> currentRegistrations;

            // Get a snapshot of current registrations to avoid lock contention
            lock (registrationsLock)
            {
                currentRegistrations = new List<UsbDeviceRegistration>(registrations);
            }

            // Check if ANY registration requires property validation
            bool needsProperties = currentRegistrations.Any(r => r.PropertyValidators != null && r.PropertyValidators.Length > 0);

            // Retrieve device properties via WMI if needed
            Dictionary<string, string> deviceProperties = null;
            if (needsProperties)
            {
                System.Diagnostics.Debug.WriteLine($"Retrieving device properties via WMI for VID:{e.VendorId} PID:{e.ProductId}");
                deviceProperties = DevicePropertyRetriever.GetDeviceProperties(e.DevicePath);

                if (deviceProperties != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Successfully retrieved {deviceProperties.Count} properties");
                    // Populate the event args with properties for the callback
                    e.Properties = deviceProperties;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to retrieve device properties for VID:{e.VendorId} PID:{e.ProductId}");
                }
            }

            // Check each registration to see if it matches this device
            foreach (var registration in currentRegistrations)
            {
                if (IsMatch(registration, e, deviceProperties))
                {
                    System.Diagnostics.Debug.WriteLine($"Match found for VID:{e.VendorId} PID:{e.ProductId}");

                    // Invoke the callback if it's registered
                    if (registration.OnConnect != null)
                    {
                        try
                        {
                            registration.OnConnect(e);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error in OnConnect callback: {ex.Message}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Called when a USB device is removed
        /// Iterates through all registrations and invokes callbacks for matching VID/PID and properties
        /// </summary>
        private void OnDeviceRemoved(object sender, UsbDeviceEventArgs e)
        {
            List<UsbDeviceRegistration> currentRegistrations;

            // Get a snapshot of current registrations to avoid lock contention
            lock (registrationsLock)
            {
                currentRegistrations = new List<UsbDeviceRegistration>(registrations);
            }

            // Check if ANY registration requires property validation
            bool needsProperties = currentRegistrations.Any(r => r.PropertyValidators != null && r.PropertyValidators.Length > 0);

            // Retrieve device properties via WMI if needed
            // Note: On device removal, WMI might not be able to retrieve properties as the device is being removed
            // This is a known limitation - property validation is more reliable on connect than disconnect
            Dictionary<string, string> deviceProperties = null;
            if (needsProperties)
            {
                System.Diagnostics.Debug.WriteLine($"Retrieving device properties via WMI for VID:{e.VendorId} PID:{e.ProductId}");
                deviceProperties = DevicePropertyRetriever.GetDeviceProperties(e.DevicePath);

                if (deviceProperties != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Successfully retrieved {deviceProperties.Count} properties");
                    // Populate the event args with properties for the callback
                    e.Properties = deviceProperties;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to retrieve device properties for VID:{e.VendorId} PID:{e.ProductId} (this is common on disconnect)");
                }
            }

            // Check each registration to see if it matches this device
            foreach (var registration in currentRegistrations)
            {
                if (IsMatch(registration, e, deviceProperties))
                {
                    System.Diagnostics.Debug.WriteLine($"Match found for VID:{e.VendorId} PID:{e.ProductId}");

                    // Invoke the callback if it's registered
                    if (registration.OnDisconnect != null)
                    {
                        try
                        {
                            registration.OnDisconnect(e);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error in OnDisconnect callback: {ex.Message}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Checks if a registration matches the device event
        /// Validates both VID/PID and optional property validators
        /// </summary>
        /// <param name="registration">The registration to check</param>
        /// <param name="e">The device event arguments</param>
        /// <param name="deviceProperties">Device properties retrieved via WMI (may be null)</param>
        /// <returns>True if all conditions match (VID/PID and all property validators), false otherwise</returns>
        private bool IsMatch(UsbDeviceRegistration registration, UsbDeviceEventArgs e, Dictionary<string, string> deviceProperties)
        {
            // First check: VID/PID must match
            if (!e.DevicePath.ToLower().Contains(registration.VidPid.ToLower()))
            {
                return false;
            }

            // Second check: If property validators are specified, they must all pass
            if (registration.PropertyValidators != null && registration.PropertyValidators.Length > 0)
            {
                // Properties must be retrieved for validation
                if (deviceProperties == null)
                {
                    // STRICT MODE: If properties couldn't be retrieved, fail the match
                    System.Diagnostics.Debug.WriteLine($"Property validation required but properties could not be retrieved for VID:{e.VendorId} PID:{e.ProductId}");
                    return false;
                }

                // Validate each property validator
                foreach (var validator in registration.PropertyValidators)
                {
                    // Check if the property exists in the retrieved properties
                    if (!deviceProperties.TryGetValue(validator.PropertyName, out string actualValue))
                    {
                        // STRICT MODE: Property doesn't exist, fail the match
                        System.Diagnostics.Debug.WriteLine($"Property '{validator.PropertyName}' not found in device properties for VID:{e.VendorId} PID:{e.ProductId}");
                        return false;
                    }

                    // Validate the property value
                    if (!validator.Validate(actualValue))
                    {
                        System.Diagnostics.Debug.WriteLine($"Property validation failed: {validator} (actual value: '{actualValue}')");
                        return false;
                    }

                    System.Diagnostics.Debug.WriteLine($"Property validation passed: {validator} (actual value: '{actualValue}')");
                }

                System.Diagnostics.Debug.WriteLine($"All {registration.PropertyValidators.Length} property validator(s) passed for VID:{e.VendorId} PID:{e.ProductId}");
            }

            return true;
        }

        /// <summary>
        /// Stops monitoring for USB device changes
        /// Closes the message window and terminates the message loop thread
        /// </summary>
        public void Stop()
        {
            if (messageWindow != null)
            {
                try
                {
                    // Close the window on its own thread if needed
                    if (messageWindow.InvokeRequired)
                    {
                        messageWindow.Invoke(new Action(() =>
                        {
                            messageWindow.Close();
                        }));
                    }
                    else
                    {
                        messageWindow.Close();
                    }
                }
                catch { }
            }

            // Wait for the message loop thread to terminate (with 5 second timeout)
            if (messageLoopThread != null && messageLoopThread.IsAlive)
            {
                messageLoopThread.Join(5000);
            }
        }

        public void Dispose()
        {
            Stop();
            if (windowCreated != null)
            {
                windowCreated.Dispose();
            }
        }

        /// <summary>
        /// Message-only window that receives WM_DEVICECHANGE messages from Windows
        ///
        /// This is an invisible Form that exists only to receive Windows messages.
        /// It uses Win32 API functions to:
        /// 1. Register for device notifications (RegisterDeviceNotification)
        /// 2. Receive messages via WndProc when devices change
        /// 3. Parse the messages and fire C# events
        /// </summary>
        private class MessageOnlyWindow : Form
        {
            // Win32 API: Registers the window to receive device notifications
            [DllImport("user32.dll", SetLastError = true)]
            static extern IntPtr RegisterDeviceNotification(IntPtr hRecipient, IntPtr NotificationFilter, uint Flags);

            // Win32 API: Unregisters device notifications when done
            [DllImport("user32.dll", SetLastError = true)]
            static extern bool UnregisterDeviceNotification(IntPtr Handle);

            // Structure sent to RegisterDeviceNotification to specify what devices to monitor
            [StructLayout(LayoutKind.Sequential)]
            struct DEV_BROADCAST_DEVICEINTERFACE
            {
                public int dbcc_size;           // Size of this structure
                public int dbcc_devicetype;     // Type of device (we use DBT_DEVTYP_DEVICEINTERFACE)
                public int dbcc_reserved;       // Reserved, must be 0
                public Guid dbcc_classguid;     // GUID for the device class (USB in our case)
                public short dbcc_name;         // Device name placeholder
            }

            // Full structure received from Windows when a device change occurs
            // Contains the actual device path and information
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            struct DEV_BROADCAST_DEVICEINTERFACE_FULL
            {
                public int dbcc_size;
                public int dbcc_devicetype;
                public int dbcc_reserved;
                public Guid dbcc_classguid;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 255)]
                public string dbcc_name;        // Device path (e.g., "\\?\USB#VID_046D&PID_C52B...")
            }

            // Windows message constants
            const int WM_DEVICECHANGE = 0x0219;              // Message sent when devices change
            const int DBT_DEVICEARRIVAL = 0x8000;            // A device has been connected
            const int DBT_DEVICEREMOVECOMPLETE = 0x8004;     // A device has been removed
            const int DBT_DEVTYP_DEVICEINTERFACE = 5;        // Device interface class
            const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;       // Notification filter flag

            // USB device class GUID - tells Windows we want notifications for USB devices
            static readonly Guid GUID_DEVINTERFACE_USB_DEVICE =
                new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED");

            // Handle returned by RegisterDeviceNotification, needed for cleanup
            private IntPtr notificationHandle;

            // Events fired when USB devices are connected or removed
            public event EventHandler<UsbDeviceEventArgs> DeviceConnected;
            public event EventHandler<UsbDeviceEventArgs> DeviceRemoved;

            /// <summary>
            /// Constructor - configures the window to be invisible
            /// </summary>
            public MessageOnlyWindow()
            {
                this.FormBorderStyle = FormBorderStyle.None;  // No border
                this.ShowInTaskbar = false;                    // Don't show in taskbar
                this.Size = new System.Drawing.Size(0, 0);     // Zero size
            }

            /// <summary>
            /// Overrides window creation parameters to make this a message-only window
            /// Setting Parent to HWND_MESSAGE (-3) creates a message-only window
            /// Message-only windows exist solely to process messages - they have no visual presence
            /// </summary>
            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.Parent = new IntPtr(-3); // HWND_MESSAGE - makes this a message-only window
                    return cp;
                }
            }

            /// <summary>
            /// Called when the window handle (HWND) is created
            /// This is the right time to register for device notifications
            /// We need a valid handle before calling RegisterDeviceNotification
            /// </summary>
            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                RegisterForDeviceNotifications();
                System.Diagnostics.Debug.WriteLine("MessageOnlyWindow handle created and registered for notifications");
            }

            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                System.Diagnostics.Debug.WriteLine("MessageOnlyWindow loaded");
            }

            /// <summary>
            /// Registers this window with Windows to receive USB device change notifications
            ///
            /// HOW IT WORKS:
            /// 1. Create a DEV_BROADCAST_DEVICEINTERFACE structure specifying USB devices
            /// 2. Marshal it to unmanaged memory (Win32 API requires a pointer)
            /// 3. Call RegisterDeviceNotification with our window handle
            /// 4. Windows will now send WM_DEVICECHANGE messages to our window when USB devices change
            /// </summary>
            private void RegisterForDeviceNotifications()
            {
                // Ensure we have a valid window handle
                if (this.Handle == IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine("Handle is zero - cannot register");
                    return;
                }

                // Create the filter structure that tells Windows what notifications we want
                DEV_BROADCAST_DEVICEINTERFACE dbi = new DEV_BROADCAST_DEVICEINTERFACE
                {
                    dbcc_size = Marshal.SizeOf(typeof(DEV_BROADCAST_DEVICEINTERFACE)),
                    dbcc_devicetype = DBT_DEVTYP_DEVICEINTERFACE,  // We want device interface notifications
                    dbcc_reserved = 0,
                    dbcc_classguid = GUID_DEVINTERFACE_USB_DEVICE, // Specifically USB devices
                    dbcc_name = 0
                };

                // Allocate unmanaged memory for the structure (required by Win32 API)
                IntPtr buffer = Marshal.AllocHGlobal(dbi.dbcc_size);
                try
                {
                    // Copy our structure to unmanaged memory
                    Marshal.StructureToPtr(dbi, buffer, true);

                    // Register with Windows to receive notifications
                    // Parameters:
                    //   - this.Handle: Our window that will receive WM_DEVICECHANGE messages
                    //   - buffer: Pointer to our filter structure
                    //   - DEVICE_NOTIFY_WINDOW_HANDLE: Flag indicating we're passing a window handle
                    notificationHandle = RegisterDeviceNotification(
                        this.Handle,
                        buffer,
                        DEVICE_NOTIFY_WINDOW_HANDLE);

                    // Check if registration succeeded
                    if (notificationHandle == IntPtr.Zero)
                    {
                        int error = Marshal.GetLastWin32Error();
                        System.Diagnostics.Debug.WriteLine("RegisterDeviceNotification failed: " + error);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Successfully registered for device notifications");
                    }
                }
                finally
                {
                    // Always free unmanaged memory to prevent leaks
                    Marshal.FreeHGlobal(buffer);
                }
            }

            /// <summary>
            /// WndProc - The Window Procedure that processes ALL Windows messages for this window
            ///
            /// This is where the magic happens! Every Windows message sent to this window comes here.
            /// We intercept WM_DEVICECHANGE messages which Windows sends when devices are plugged/unplugged.
            ///
            /// MESSAGE FLOW:
            /// 1. Windows detects a USB device change
            /// 2. Because we registered with RegisterDeviceNotification, Windows sends WM_DEVICECHANGE to our window
            /// 3. This method receives the message
            /// 4. We parse the message data to extract device information (VID, PID, path)
            /// 5. We fire C# events that consumers can subscribe to
            ///
            /// MESSAGE STRUCTURE:
            /// - m.Msg: The message ID (WM_DEVICECHANGE = 0x0219)
            /// - m.WParam: Event type (arrival, removal, etc.)
            /// - m.LParam: Pointer to DEV_BROADCAST_DEVICEINTERFACE_FULL structure with device details
            /// </summary>
            protected override void WndProc(ref Message m)
            {
                // Check if this is a device change message
                if (m.Msg == WM_DEVICECHANGE)
                {
                    // Extract the event type from WParam
                    // This tells us what kind of device change occurred
                    int eventType = m.WParam.ToInt32();
                    System.Diagnostics.Debug.WriteLine("WM_DEVICECHANGE received in MessageOnlyWindow! EventType: " + eventType);

                    // We only care about device arrival and removal events
                    if (eventType == DBT_DEVICEARRIVAL || eventType == DBT_DEVICEREMOVECOMPLETE)
                    {
                        // LParam contains a pointer to device information
                        if (m.LParam != IntPtr.Zero)
                        {
                            // Marshal the unmanaged data into our C# structure
                            // This converts the raw memory pointer into a usable object
                            DEV_BROADCAST_DEVICEINTERFACE_FULL deviceInfo =
                                (DEV_BROADCAST_DEVICEINTERFACE_FULL)Marshal.PtrToStructure(
                                    m.LParam,
                                    typeof(DEV_BROADCAST_DEVICEINTERFACE_FULL));

                            // Verify this is a device interface notification (not volume, port, etc.)
                            if (deviceInfo.dbcc_devicetype == DBT_DEVTYP_DEVICEINTERFACE)
                            {
                                // Extract device information from the device path
                                // Device path format: \\?\USB#VID_046D&PID_C52B#...
                                string devicePath = deviceInfo.dbcc_name;
                                string vid = ExtractValue(devicePath, "VID_");  // Vendor ID
                                string pid = ExtractValue(devicePath, "PID_");  // Product ID

                                // Create event args with the extracted information
                                var eventArgs = new UsbDeviceEventArgs
                                {
                                    DevicePath = devicePath,
                                    VendorId = vid,
                                    ProductId = pid
                                };

                                // Fire the appropriate C# event based on the event type
                                if (eventType == DBT_DEVICEARRIVAL)
                                {
                                    System.Diagnostics.Debug.WriteLine("Firing DeviceConnected event");
                                    if (DeviceConnected != null)
                                        DeviceConnected(this, eventArgs);
                                }
                                else if (eventType == DBT_DEVICEREMOVECOMPLETE)
                                {
                                    System.Diagnostics.Debug.WriteLine("Firing DeviceRemoved event");
                                    if (DeviceRemoved != null)
                                        DeviceRemoved(this, eventArgs);
                                }
                            }
                        }
                    }
                }

                // Always call base.WndProc to ensure other messages are processed normally
                base.WndProc(ref m);
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
            /// <returns>The extracted value, or "Unknown" if not found</returns>
            private string ExtractValue(string path, string prefix)
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
                    // If anything goes wrong, just return Unknown
                }
                return "Unknown";
            }

            /// <summary>
            /// Cleanup - unregister from Windows device notifications when the window is disposed
            /// This is important to prevent memory leaks and ensure proper cleanup
            /// </summary>
            protected override void Dispose(bool disposing)
            {
                if (notificationHandle != IntPtr.Zero)
                {
                    // Unregister from device notifications
                    UnregisterDeviceNotification(notificationHandle);
                    notificationHandle = IntPtr.Zero;
                }
                base.Dispose(disposing);
            }
        }
    }

    /// <summary>
    /// Represents a registration for USB device monitoring
    /// Contains the VID/PID filter, property validators, and callbacks for a specific listener
    /// </summary>
    public class UsbDeviceRegistration
    {

        internal string VidPid { get; set; }

        /// <summary>
        /// Callback invoked when a matching device is connected
        /// </summary>
        internal UsbDeviceConnectedCallback OnConnect { get; set; }

        /// <summary>
        /// Callback invoked when a matching device is disconnected
        /// </summary>
        internal UsbDeviceDisconnectedCallback OnDisconnect { get; set; }

        /// <summary>
        /// Optional property validators that must all pass for callbacks to be invoked
        /// If null or empty, only VID/PID matching is performed
        /// </summary>
        internal PropertyValidator[] PropertyValidators { get; set; }
    }

    /// <summary>
    /// Event arguments containing USB device information
    ///
    /// This is passed to event handlers when a USB device is connected or removed
    /// </summary>
    public class UsbDeviceEventArgs : EventArgs
    {
        /// <summary>
        /// The full Windows device path (e.g., "\\?\USB#VID_046D&PID_C52B#...")
        /// </summary>
        public string DevicePath { get; set; }

        /// <summary>
        /// Vendor ID - a 4-digit hex value identifying the device manufacturer
        /// Example: "046D" for Logitech
        /// </summary>
        public string VendorId { get; set; }

        /// <summary>
        /// Product ID - a 4-digit hex value identifying the specific product
        /// Example: "C52B" for a specific Logitech mouse model
        /// </summary>
        public string ProductId { get; set; }

        /// <summary>
        /// Dictionary of device properties retrieved via WMI
        /// Only populated when property validation is used in the registration
        /// Common properties: Name, FriendlyName, Manufacturer, Description, Status, etc.
        /// May be null if no property validators were specified or if property retrieval failed
        /// </summary>
        public Dictionary<string, string> Properties { get; set; }
    }
}