using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace VideoDB
{
    public partial class AddCapturedEventWindow : Window
    {
        private readonly ObservableCollection<string> selectedTags = new ObservableCollection<string>();
        private readonly ObservableCollection<string> selectedActresses = new ObservableCollection<string>();

        public AddCapturedEventWindow(
            string videoId,
            string time,
            List<string> tags,
            List<string> actresses,
            List<string> recentTags,
            List<string> recentActresses,
            List<MainWindow.SceneItem> existingScenes)
        {
            InitializeComponent();

            VideoIdBox.Text = videoId;
            TimeBox.Text = time;
            TagBox.ItemsSource = tags;
            ActressBox.ItemsSource = actresses;
            RecentTagItemsControl.ItemsSource = recentTags;
            RecentActressItemsControl.ItemsSource = recentActresses;
            TagChipItemsControl.ItemsSource = selectedTags;
            ActressChipItemsControl.ItemsSource = selectedActresses;
            ExistingSceneItemsControl.ItemsSource = existingScenes;
            NoExistingScenesTextBlock.Visibility =
                existingScenes.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        public string EventTime => TimeBox.Text.Trim();

        public List<string> EventTags => selectedTags.ToList();

        public List<string> EventActresses => selectedActresses.ToList();

        public string EventNote => NoteBox.Text.Trim();

        public int EventRating
        {
            get
            {
                ComboBoxItem? item = RatingBox.SelectedItem as ComboBoxItem;

                if (item == null)
                    return 3;

                return int.Parse(item.Content.ToString() ?? "3");
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            TagBox.Focus();
        }

        private void AddTagButton_Click(object sender, RoutedEventArgs e)
        {
            AddToken(TagBox.Text, selectedTags);
            TagBox.Text = string.Empty;
            TagBox.Focus();
        }

        private void AddActressButton_Click(object sender, RoutedEventArgs e)
        {
            AddToken(ActressBox.Text, selectedActresses);
            ActressBox.Text = string.Empty;
            ActressBox.Focus();
        }

        private void RecentTagButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Content is string tag)
            {
                AddToken(tag, selectedTags);
                TagBox.Focus();
            }
        }

        private void RecentActressButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Content is string actress)
            {
                AddToken(actress, selectedActresses);
                ActressBox.Focus();
            }
        }

        private void RemoveTagButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Content is string tag)
            {
                selectedTags.Remove(tag);
            }
        }

        private void RemoveActressButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Content is string actress)
            {
                selectedActresses.Remove(actress);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private static void AddToken(string value, ObservableCollection<string> target)
        {
            string token = value.Trim();

            if (string.IsNullOrWhiteSpace(token))
                return;

            bool exists = target.Any(x => string.Equals(x, token, StringComparison.OrdinalIgnoreCase));

            if (!exists)
            {
                target.Add(token);
            }
        }
    }
}
