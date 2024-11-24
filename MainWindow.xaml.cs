using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml;

namespace XMLEdit
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        UserFile file = new UserFile();

        public MainWindow()
        {
            InitializeComponent();
            setAppTitle();
            //clusterListView.SizeChanged += ListView_SizeChanged;
            //AdjustColumnWidths();

            StartRotationAnimation();
        }

        private void setAppTitle()
        {
            string location = file.Path == string.Empty ? "Файл не выбран" : file.Path;
            Dispatcher.Invoke(() =>
            {
                Title = $"XMLEditorDZ -  [{location}]";
            });
        }

        private async void openDialog_Click(object sender, RoutedEventArgs e)
        {
            openDialog.Dispatcher.Invoke(() => { openDialog.IsEnabled = false; });

            showLoading(true);

            OpenFileDialog dialog = new OpenFileDialog();
            dialog.CheckFileExists = true;
            dialog.CheckPathExists = true;
            dialog.Filter = "XML|*.xml";
            bool? result = dialog.ShowDialog();

            if (result == true)
            {
                await Task.Run(() => openFile(dialog.FileNames[0]));
                if(file.ReadError == string.Empty) showNotify("Успешно", "Файл прочитан", TimeSpan.FromSeconds(2));
                else showNotify("Ошибка чтения", file.ReadError, TimeSpan.FromSeconds(5), 450);
            }
            else showNotify("Ошибка", "Файл не был открыт", TimeSpan.FromSeconds(2.5));
            showLoading(false);

            openDialog.Dispatcher.Invoke(() => { openDialog.IsEnabled = true; });
        }

        private void updateFileInfo()
        {
            fileSize.Dispatcher.Invoke(() => { fileSize.Text = $"Размер файла: {file.Size}"; });
            fileLines.Dispatcher.Invoke(() => { fileLines.Text = $"Строк загружено: {file.Lines}"; });
            fileGroupCount.Dispatcher.Invoke(() => { fileGroupCount.Text = $"Количество групп: {file.GroupCount}"; });
        }

        private Task loadFile()
        {
            file.Size = $"{Math.Round(((double)new FileInfo(file.Path).Length / 1024 / 1024) * 100) / 100}МБ";

            Dispatcher.Invoke(() => {
                file.ClusterList.Clear();
                file.IncludeList.Clear();
            });

            //clusterListView.Dispatcher.Invoke(() => { clusterListView.Items.Refresh(); });

            file.GroupCount = 0;

            file.ReadError = string.Empty;

            Task.Run(() =>
            {
                int lineCount = 0;
                using (StreamReader reader = new StreamReader(file.Path))
                {
                    file.XMLString = reader.ReadLine();
                    file.XMLBody = reader.ReadLine();

                    string content = reader.ReadToEnd();
                    lineCount = content.Count(c => c == '\n') + 1;
                }
                file.Lines = lineCount;
            });

            try
            {
                using (XmlReader reader = XmlReader.Create(file.Path))
                {
                    Dispatcher.Invoke(() =>
                    {
                        while (reader.Read())
                        {
                            if (reader.NodeType == XmlNodeType.Element && reader.Name == "group")
                            {
                                Cluster item = new Cluster(reader.GetAttribute("name"), reader.GetAttribute("pos"), reader.GetAttribute("a"));
                                item.SetID(file.GroupCount + 1);

                                file.ClusterList.Add(item);

                                file.GroupCount++;
                            }
                            else if (reader.NodeType == XmlNodeType.Element && reader.Name == "include")
                            {
                                file.IncludeList.Add(new Include(reader.GetAttribute("file")));
                            }
                        }
                    });
                }
            } catch(Exception ex)
            {
                file.ReadError = ex.Message;
            }

            //clusterListView.Dispatcher.Invoke(() => { clusterListView.Items.Refresh(); });

            return Task.CompletedTask;
        }

        private Task configureListView()
        {
            clusterListView.Dispatcher.Invoke(() =>
            {
                clusterListView.ItemsSource = file.ClusterList;
            });
            return Task.CompletedTask;
        }

        //private void ListView_SizeChanged(object sender, SizeChangedEventArgs e)
        //{
        //    AdjustColumnWidths();
        //}

        //private void AdjustColumnWidths()
        //{
        //    var gridView = clusterListView.View as GridView;
        //    if (gridView != null)
        //    {
        //        double totalWidth = clusterListView.ActualWidth;
        //        //int columnCount = gridView.Columns.Count;
        //        double columnWidth = totalWidth - gridView.Columns[0].Width - gridView.Columns[1].Width - gridView.Columns[3].Width;

        //        if(columnWidth > 0) gridView.Columns[2].Width = columnWidth - 50;
        //        else gridView.Columns[2].Width = 500;

        //    }
        //}

        private async void saveFile_Click(object sender, RoutedEventArgs e)
        {
            showLoading(true);

            try
            {
                string message = "Файл сохранён";

                if (createBackup.IsChecked == true)
                {
                    string backupPath = file.Path + ".bak";

                    if (File.Exists(backupPath)) backupPath = backupPath + "_" + md5(DateTime.Now.ToString());

                    File.Move(file.Path, backupPath);

                    message += $"\nBACKUP: {backupPath}";
                }

                if (!File.Exists(file.Path)) File.Create(file.Path).Close();

                await Task.Run(writeFile);

                if(file.WriteError == string.Empty) showNotify("Успешно", message, TimeSpan.FromSeconds(5), 450);
                else showNotify("Ошибка записи", file.WriteError, TimeSpan.FromSeconds(5), 450);
                
            }
            catch(Exception ex)
            {
                showNotify("Ошибка записи", ex.Message, TimeSpan.FromSeconds(5), 450);
            }
            showLoading(false);
        }

        private Task writeFile()
        {
            file.WriteError = string.Empty;

            try
            {
                using (StreamWriter writer = new StreamWriter(file.Path))
                {
                    writer.WriteLine($"{file.XMLString}");
                    writer.WriteLine($"{file.XMLBody}");

                    foreach (Cluster item in file.ClusterList)
                    {
                        writer.WriteLine($"\t<group name=\"{item.Name}\" pos=\"{item.Pos}\" a=\"{item.A}\" />");
                    }

                    foreach (Include item in file.IncludeList)
                    {
                        writer.WriteLine($"\t<include file=\"{item.File}\" />");
                    }

                    writer.WriteLine($"{file.XMLBody.Replace("<", "</")}");
                }
            }
            catch (Exception ex)
            {
                file.WriteError = ex.Message;
            }

            return Task.CompletedTask;
        }

        private async Task openFile(string path)
        {
            file.Path = path;
            xmlLocation.Dispatcher.Invoke(() =>
            {
                xmlLocation.Text = file.Path;
            });
            setAppTitle();
            saveFile.Dispatcher.Invoke(() =>
            {
                saveFile.IsEnabled = true;
            });
            await Task.Run(loadFile);
            await Task.Run(configureListView);
            updateFileInfo();
        }

        private async void Startup(object sender, EventArgs e)
        {
            string[] args = Environment.GetCommandLineArgs();

            if (args == null || args.Length == 1) return;

            showLoading(true);

            await Task.Run(() => openFile(args[1]));

            if (file.ReadError == string.Empty) showNotify("Успешно", "Файл прочитан", TimeSpan.FromSeconds(2));
            else showNotify("Ошибка чтения", file.ReadError, TimeSpan.FromSeconds(5), 450);

            showLoading(false);
        }

        public static string md5(string input)
        {
            using (MD5 md5Hash = MD5.Create())
            {
                byte[] data = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder sBuilder = new StringBuilder();
                for (int i = 0; i < data.Length; i++)
                {
                    sBuilder.Append(data[i].ToString("x2"));
                }
                return sBuilder.ToString();
            }
        }

        private void showNotify(string title, string content, TimeSpan closeAfter, int width = 250)
        {
            notify.Dispatcher.Invoke(() => { notify.Visibility = Visibility.Visible; });

            notifyTitle.Dispatcher.Invoke(() => { notifyTitle.Text = title; });
            notifyContent.Dispatcher.Invoke(() => { notifyContent.Text = content; });
            notify.Dispatcher.Invoke(() => { notify.Width = width; });

            DispatcherTimer _notifyCloseHandler = new DispatcherTimer();
            _notifyCloseHandler.Tick += (object sender, EventArgs e) =>
            {
                notify.Dispatcher.Invoke(() => { notify.Visibility = Visibility.Collapsed; });
                notify.Dispatcher.Invoke(() => { notify.Width = 250; });
                _notifyCloseHandler.Stop();
            };
            _notifyCloseHandler.Dispatcher.Invoke(() => { _notifyCloseHandler.Interval = closeAfter; });
            _notifyCloseHandler.Start();
        }

        private void showLoading(bool isShow)
        {
            if(isShow)
            {
                loadingControls.Dispatcher.Invoke(() => { loadingControls.Visibility = Visibility.Visible; });
                loadingList.Dispatcher.Invoke(() => { loadingList.Visibility = Visibility.Visible; });
            }
            else
            {
                loadingControls.Dispatcher.Invoke(() => { loadingControls.Visibility = Visibility.Collapsed; });
                loadingList.Dispatcher.Invoke(() => { loadingList.Visibility = Visibility.Collapsed; });
            }
        }

        private void StartRotationAnimation()
        {
            DoubleAnimation rotateAnimation = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = new Duration(TimeSpan.FromSeconds(1)),
                RepeatBehavior = RepeatBehavior.Forever
            };
            RotateTransform.BeginAnimation(RotateTransform.AngleProperty, rotateAnimation);
        }

        private void notifyClose_Click(object sender, RoutedEventArgs e)
        {
            notify.Dispatcher.Invoke(() => { notify.Visibility = Visibility.Collapsed; });
        }

        private void applyQuery_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(() => {
                file.ClusterList[0].Select(SelectColor.Red.String());
                file.ClusterList[2].Select(SelectColor.Yellow.String());
                file.ClusterList[4].Select(SelectColor.Blue.String());
                file.ClusterList[6].Select(SelectColor.Green.String());
            });
        }
    }

    public enum SelectColor
    {
        Red = 0,
        Green = 1,
        Yellow = 2,
        Blue = 3
    }

    public static class ColorExtensions
    {
        public static string String(this SelectColor color)
        {
            switch (color)
            {
                case SelectColor.Red: return "#7FFF0000";
                case SelectColor.Green: return "#7F08FF00";
                case SelectColor.Yellow: return "#7FFDFF00";
                case SelectColor.Blue: return "#7F0800FF";
                default: return "Transparent";
            }
        }
    }

    public class Position
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public Position(double[] array)
        {
            X = array[0]; Y = array[1]; Z = array[2];
        }

        public override string ToString()
        {
            return $"{X} {Y} {Z}".Replace(',', '.');
        }
    }

    public class Include
    {
        public string File { get; set; } = "";

        public Include(string file)
        {
            File = file;
        }
    }

    public class Cluster : INotifyPropertyChanged
    {
        private int _ID;
        private string _Name;
        private string _Pos;
        private string _A;
        private string _Color;
        private bool _Selected;

        public event PropertyChangedEventHandler PropertyChanged;

        public bool Selected
        {
            get => _Selected;
            set
            {
                if (value != _Selected)
                {
                    _Selected = value;
                    OnPropertyChanged();
                }
            }
        }

        public int ID
        {
            get => _ID;
            set
            {
                if (value != _ID)
                {
                    _ID = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Name
        {
            get => _Name;
            set
            {
                if (value != _Name)
                {
                    _Name = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Pos
        {
            get => _Pos;
            set
            {
                if (value != _Pos)
                {
                    _Pos = value;
                    OnPropertyChanged();
                }
            }
        }

        public string A
        {
            get => _A;
            set
            {
                if (value != _A)
                {
                    _A = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Color
        {
            get => _Color;
            set
            {
                if (value != _Color)
                {
                    _Color = value;
                    OnPropertyChanged();
                }
            }
        }

        public Cluster(string name, string pos, string a)
        {
            Name = name;
            Pos = pos;
            A = a;
            Color = "Transparent";
            Selected = false;
        }

        public void Select(string color = "Transparent")
        {
            Selected = true;
            Color = color;
        }

        public void UnSelect()
        {
            Selected = false;
            Color = "Transparent";
        }

        public void SetID(int id) => ID = id;

        public Position PositionTranslate()
        {
            string[] stringArray = Pos.Split(' ');
            double[] doubleArray = stringArray.Select(x => double.Parse(x.Replace('.', ','))).ToArray();
            return new Position(doubleArray);
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class UserFile
    {
        public string Path { get; set; } = string.Empty;
        public int Lines { get; set; } = 0;
        public string Size { get; set; } = string.Empty;
        public int GroupCount { get; set; } = 0;
        public ObservableCollection<Cluster> ClusterList { get; set; } = new ObservableCollection<Cluster>();
        public List<Include> IncludeList { get; set; } = new List<Include>();
        public string XMLString { get; set; } = "";
        public string XMLBody { get; set; } = "";
        public string ReadError { get; set; } = string.Empty;
        public string WriteError { get; set; } = string.Empty;
    }
}
