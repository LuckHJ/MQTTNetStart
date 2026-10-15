using System;
using System.Windows;
using System.Windows.Input;

namespace CabinetFaceWpf;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 24;
        Top = workArea.Bottom - Height - 24;
    }

    private void Pet_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OpenCameraMenu_Click(object sender, RoutedEventArgs e)
    {
        var cameraWindow = new CameraWindow { Owner = this };
        cameraWindow.ShowDialog();
    }

    private void OpenSudokuMenu_Click(object sender, RoutedEventArgs e)
    {
        var sudokuWindow = new SudokuWindow { Owner = this };
        sudokuWindow.Show();
    }

    private void ExitMenu_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
