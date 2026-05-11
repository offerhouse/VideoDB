using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;

namespace VideoDB
{
    public partial class ScanLocationsWindow : Window
    {
        private readonly ObservableCollection<ScanLocationItem> locations;

        public ScanLocationsWindow(List<ScanLocationItem> scanLocations, string mpcPath)
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
        }

        public List<ScanLocationItem> ScanLocations => locations.ToList();

        public string MpcPath => MpcPathBox.Text.Trim();

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

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
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
