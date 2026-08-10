﻿using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using TCRSaveEditor.Models;
using System.Linq;

namespace TCRSaveEditor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public ObservableCollection<City> Cities { get; set; } = new();
        public ObservableCollection<City> FilteredCities { get; set; } = new();
        public ObservableCollection<string> Factions { get; set; } = new();
        public GameMeta GameMeta { get; set; } = new();
        private string? _currentSavePath;
        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
        }
        private void action_install_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSavePath == null)
            {
                MessageBox.Show("请先打开一个存档文件。");
                return;
            }
            var editedFileName = System.IO.Path.GetFileNameWithoutExtension(_currentSavePath) + "_edited.sav";
            var outPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_currentSavePath)!, editedFileName);
            if (!File.Exists(outPath))
            {
                MessageBox.Show("没有找到编辑后的存档文件。您保存修改了吗？");
                return;
            }
            string[] TCRSEBack = { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TotalConflictResistance", "Saved", "SaveGames", "TCRSEBackup" };
            string backPath = System.IO.Path.Combine(TCRSEBack);
            if (!Directory.Exists(backPath))
            {
                try
                {
                    Directory.CreateDirectory(backPath);
                    
                }
                 catch (UnauthorizedAccessException err)
                {
                    MessageBox.Show("无法创建备份路径：访问被拒绝", backPath);
                    return;
                }
            }
            string backupFile = System.IO.Path.Combine(backPath, System.IO.Path.GetFileName(_currentSavePath));
            if (!File.Exists(backupFile))
            {
                File.Move(_currentSavePath, backupFile);
            }
            try
            {
                File.Move(outPath, _currentSavePath, true);
                MessageBox.Show("修改已成功安装。");
            }
            catch (Exception err)
            {
                MessageBox.Show("重命名文件时发生错误：", err.Message);
            }
        }
        private void action_savechanges_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSavePath == null)
            {
                MessageBox.Show("请先打开一个存档文件。");
                return;
            }
            var editedFileName = System.IO.Path.GetFileNameWithoutExtension(_currentSavePath) + "_edited.sav";
            var outPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_currentSavePath)!, editedFileName);
            try
            {
                TCRSaveEditor.Services.GvasWriter.WriteChanges(_currentSavePath, outPath, Cities, GameMeta);
                MessageBox.Show($"保存并验证成功: {outPath}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败:\n{ex.Message}");
            }
            //var saveDlg = new Microsoft.Win32.SaveFileDialog
            //{
            //    FileName = System.IO.Path.GetFileNameWithoutExtension(_currentSavePath) + "_edited.sav",
            //    InitialDirectory = System.IO.Path.GetDirectoryName(_currentSavePath),
            //    DefaultExt = ".sav",
            //    Filter = "TCR Save Files (.sav)|*.sav"
            //};

            //if (saveDlg.ShowDialog() == true)
            //{
            //    try
            //    {
            //        TCRSaveEditor.Services.GvasWriter.WriteChanges(_currentSavePath, saveDlg.FileName, Cities, GameMeta);
            //        MessageBox.Show($"Saved and verified: {saveDlg.FileName}");
            //    }
            //    catch (Exception ex)
            //    {
            //        MessageBox.Show($"Save failed:\n{ex.Message}");
            //    }
            //}
        }
        private void action_opensave_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            string[] paths = { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TotalConflictResistance", "Saved", "SaveGames" };
            string fullPath = System.IO.Path.Combine(paths);
            if (Directory.Exists(fullPath))
                dlg.InitialDirectory = fullPath;

            dlg.DefaultExt = ".sav";
            dlg.Filter = "TCR Save Files (.sav)|*.sav";
            bool? result = dlg.ShowDialog();

            if (result == true)
            {
                try
                {
                    TCRSaveEditor.Services.GvasReader.LoadSaveFile(dlg.FileName, Cities, GameMeta);
                    _currentSavePath = dlg.FileName;
                    Factions.Clear();
                    Factions.Add("全部");
                    foreach (var faction in Cities.Select(c => c.Faction).Distinct().OrderBy(f => f))
                        Factions.Add(faction);

                    FactionFilterCombo.SelectedIndex = 0; // triggers SelectionChanged -> populates FilteredCities
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"无法加载该存档文件:\n{ex.Message}");
                }
            }
        }

        private void FactionFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FactionFilterCombo.SelectedItem is string selected)
            {
                FilteredCities.Clear();
                var matching = selected == "所有" ? Cities : Cities.Where(c => c.Faction == selected);
                foreach (var city in matching)
                    FilteredCities.Add(city);
            }
        }
        private void RemoveResource_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is Resource resource)
            {
                if (CitiesGrid.SelectedItem is City city)
                    city.Resources.Remove(resource);
            }
        }

        private void AddResource_Click(object sender, RoutedEventArgs e)
        {
            if (CitiesGrid.SelectedItem is City city)
            {
                var name = NewResourceNameBox.Text?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    MessageBox.Show("请输入资源名称。");
                    return;
                }
                city.Resources.Add(new Resource { Name = name, Count = 0 });
                NewResourceNameBox.Clear();
            }
            else
            {
                MessageBox.Show("请先选择一个城市。");
            }
        }

        private void ViewHelp_Click(object sender, RoutedEventArgs e)
        {
            string helpPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Help.md");
            if (!File.Exists(helpPath))
            {
                MessageBox.Show("帮助文件不存在。");
                return;
            }

            string markdownText = File.ReadAllText(helpPath);
            var engine = new MdXaml.Markdown();
            var document = engine.Transform(markdownText);
            document.FontFamily = new System.Windows.Media.FontFamily("微软雅黑");

            var helpWindow = new Window
            {
                Title = "帮助",
                Width = 600,
                Height = 500,
                Content = new System.Windows.Controls.FlowDocumentScrollViewer { Document = document }
            };
            helpWindow.ShowDialog();
        }
    }
}