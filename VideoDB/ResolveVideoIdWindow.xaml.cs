using System.Windows;

namespace VideoDB
{
    public partial class ResolveVideoIdWindow : Window
    {
        public ResolveVideoIdWindow(string currentId)
        {
            InitializeComponent();

            CurrentIdBox.Text = currentId;
            NewIdBox.Text = currentId;
        }

        public string NewId => NewIdBox.Text.Trim();

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            NewIdBox.Focus();
            NewIdBox.SelectAll();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
