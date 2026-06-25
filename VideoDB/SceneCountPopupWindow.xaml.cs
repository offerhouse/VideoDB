using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VideoDB
{
    public partial class SceneCountPopupWindow : Window
    {
        public event EventHandler? CaptureRequested;
        public event EventHandler? SceneCountPressed;
        public event EventHandler? SceneCountReleased;
        public event EventHandler? RememberPositionRequested;
        public event EventHandler? ReturnToPositionRequested;
        public event EventHandler<int>? SeekRequested;
        public event EventHandler? CloseRequested;

        public SceneCountPopupWindow()
        {
            InitializeComponent();
        }

        public void SetText(string countText, string detailText)
        {
            NumberTextBlock.Text = countText;
            DetailTextBlock.Text = detailText;
        }

        public void SetIdleText()
        {
            NumberTextBlock.Text = "-";
            DetailTextBlock.Text = "Hold Scenes to check";
        }

        public void SetPlaybackStatus(string text)
        {
            PlaybackStatusTextBlock.Text = text;
        }

        private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed)
                return;

            DragMove();
        }

        private void CaptureButton_Click(object sender, RoutedEventArgs e)
        {
            CaptureRequested?.Invoke(this, EventArgs.Empty);
        }

        private void SceneCountButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is UIElement element)
                element.CaptureMouse();

            SceneCountPressed?.Invoke(this, EventArgs.Empty);
        }

        private void SceneCountButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is UIElement element)
                element.ReleaseMouseCapture();

            SceneCountReleased?.Invoke(this, EventArgs.Empty);
        }

        private void SceneCountButton_MouseLeave(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                return;

            SceneCountReleased?.Invoke(this, EventArgs.Empty);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void RememberPositionButton_Click(object sender, RoutedEventArgs e)
        {
            RememberPositionRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ReturnToPositionButton_Click(object sender, RoutedEventArgs e)
        {
            ReturnToPositionRequested?.Invoke(this, EventArgs.Empty);
        }

        private void SeekButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                int.TryParse(button.Tag?.ToString(), out int seconds))
            {
                SeekRequested?.Invoke(this, seconds);
            }
        }
    }
}
