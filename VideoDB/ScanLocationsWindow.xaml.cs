using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;

namespace VideoDB
{
    public partial class ScanLocationsWindow : Window
    {
        private readonly ObservableCollection<ScanLocationItem> locations;

        public ScanLocationsWindow(
            List<ScanLocationItem> scanLocations,
            string mpcPath,
            string theme,
            string playerMode,
            string vlcPath,
            string vlcHost,
            string vlcPort,
            string vlcPassword)
        {
            InitializeComponent();

            locations = new ObservableCollection<ScanLocationItem>(
                scanLocations.Select(x => new ScanLocationItem()
                {
                    ID = x.ID,
                    Path = x.Path,
                    IncludeSubdirectories = x.IncludeSubdirectories,
                    Enabled = x.Enabled,
                    LastError = x.LastError
                })
            );

            LocationGrid.ItemsSource = locations;
            MpcPathBox.Text = mpcPath;
            VlcPathBox.Text = vlcPath;
            VlcHostBox.Text = vlcHost;
            VlcPortBox.Text = vlcPort;
            VlcPasswordBox.Password = vlcPassword;
            PlayerModeBox.SelectedIndex =
                string.Equals(playerMode, "VLC", StringComparison.OrdinalIgnoreCase) ? 2 :
                string.Equals(playerMode, "MPC", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            ThemeBox.SelectedIndex =
                string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 0;
        }

        public List<ScanLocationItem> ScanLocations => locations.ToList();

        public string MpcPath => MpcPathBox.Text.Trim();
        public string VlcPath => VlcPathBox.Text.Trim();
        public string VlcHost => VlcHostBox.Text.Trim();
        public string VlcPort => VlcPortBox.Text.Trim();
        public string VlcPassword => VlcPasswordBox.Password;

        public string PlayerMode
        {
            get
            {
                if (PlayerModeBox.SelectedItem is System.Windows.Controls.ComboBoxItem item)
                    return item.Content?.ToString() ?? "Auto";

                return "Auto";
            }
        }

        public string Theme
        {
            get
            {
                if (ThemeBox.SelectedItem is System.Windows.Controls.ComboBoxItem item)
                    return item.Content?.ToString() ?? "Dark";

                return "Dark";
            }
        }

        private void AddFolderButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog dialog = new OpenFolderDialog()
            {
                Title = "Select scan folder"
            };

            if (dialog.ShowDialog(this) != true)
                return;

            string path = NormalizePath(dialog.FolderName);

            bool exists =
                locations.Any(x => string.Equals(NormalizePath(x.Path), path, StringComparison.OrdinalIgnoreCase));

            if (exists)
                return;

            locations.Add(new ScanLocationItem()
            {
                Path = path,
                IncludeSubdirectories = true,
                Enabled = true
            });
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (LocationGrid.SelectedItem is ScanLocationItem selected)
            {
                locations.Remove(selected);
            }
        }

        private void BrowseMpcButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog()
            {
                Title = "Select MPC-HC executable",
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                FileName = "mpc-hc64.exe"
            };

            if (dialog.ShowDialog(this) == true)
            {
                MpcPathBox.Text = dialog.FileName;
            }
        }

        private void BrowseVlcButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog()
            {
                Title = "Select VLC executable",
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                FileName = "vlc.exe"
            };

            if (dialog.ShowDialog(this) == true)
                VlcPathBox.Text = dialog.FileName;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(VlcPort, out int port) || port < 1 || port > 65535)
            {
                MessageBox.Show("VLC port must be between 1 and 65535.");
                return;
            }

            DialogResult = true;
        }

        private static string NormalizePath(string path)
        {
            return System.IO.Path.GetFullPath(path).TrimEnd(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar
            );
        }
    }

    public class ScanLocationItem
    {
        public int ID { get; set; }
        public string Path { get; set; } = string.Empty;
        public bool IncludeSubdirectories { get; set; }
        public bool Enabled { get; set; }
        public string LastError { get; set; } = string.Empty;
    }
}
