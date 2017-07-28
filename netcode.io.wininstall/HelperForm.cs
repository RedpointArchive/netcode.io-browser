using Microsoft.Win32;
using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace netcode.io.wininstall
{
    public partial class HelperForm : Form
    {
        public HelperForm()
        {
            InitializeComponent();

            var installThread = new Thread(InstallNetcode);
            installThread.IsBackground = true;
            installThread.Start();
        }

        private void InstallNetcode()
        {
            try
            {
                Thread.Sleep(1000);

                var packageName = "netcode.io.wininstall.package.zip";
                var packageStream = Assembly.GetEntryAssembly().GetManifestResourceStream(packageName);

                var netcodePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "netcode.io");
                Directory.CreateDirectory(netcodePath);
                var packageZip = Path.Combine(netcodePath, "package.zip");
                using (var zipStream = new FileStream(packageZip, FileMode.Create))
                {
                    packageStream.CopyTo(zipStream);
                }

                using (var archive = ZipFile.OpenRead(packageZip))
                {
                    foreach (var entry in archive.Entries)
                    {
                        entry.ExtractToFile(
                            Path.Combine(netcodePath, entry.FullName),
                            true);
                    }
                }

                var registryKey = Registry.CurrentUser
                    .CreateSubKey("Software")
                    .CreateSubKey("Google")
                    .CreateSubKey("Chrome")
                    .CreateSubKey("NativeMessagingHosts")
                    .CreateSubKey("netcode.io");
                registryKey.SetValue("(Default)", Path.Combine(netcodePath, "manifest.windows.relative.json"));

                Thread.Sleep(1000);
            }
            finally
            {
                Invoke(new Action(() =>
                {
                    Close();
                }));
            }
        }
    }
}
