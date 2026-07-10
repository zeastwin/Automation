using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Automation.Peripheral
{
    public partial class AlarmHistoryPage : Form
    {
        public AlarmHistoryPage()
        {
            InitializeComponent();
            txtPath.Text = AlarmHistoryCsvReader.DefaultLogDirectory;
        }

        internal void LoadSelectedDate()
        {
            try
            {
                gridAlarmHistory.DataSource = AlarmHistoryCsvReader.Read(txtPath.Text, dtpDate.Value.Date);
            }
            catch (Exception ex)
            {
                gridAlarmHistory.DataSource = null;
                MessageBox.Show(FindForm(), ex.Message, "报警历史读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnChoosePath_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                if (Directory.Exists(txtPath.Text))
                {
                    dialog.SelectedPath = txtPath.Text;
                }
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    txtPath.Text = dialog.SelectedPath;
                }
            }
        }

        private void btnRead_Click(object sender, EventArgs e)
        {
            LoadSelectedDate();
        }

        private sealed class AlarmHistoryRecord
        {
            public string AlarmCode { get; set; }
            public string AlarmContent { get; set; }
            public string AlarmCategory { get; set; }
            public string StartTime { get; set; }
            public string EndTime { get; set; }
            public string Duration { get; set; }
            public string Location { get; set; }
        }

        private static class AlarmHistoryCsvReader
        {
            internal static string DefaultLogDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "AlarmHistory");

            internal static IReadOnlyList<AlarmHistoryRecord> Read(string directory, DateTime date)
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    throw new ArgumentException("报警历史读取路径不能为空。", nameof(directory));
                }
                string fullDirectory = Path.GetFullPath(directory);
                if (!Directory.Exists(fullDirectory))
                {
                    return new List<AlarmHistoryRecord>();
                }

                string filePath = Path.Combine(fullDirectory, date.ToString("yyyyMMdd") + ".csv");
                if (!File.Exists(filePath))
                {
                    return new List<AlarmHistoryRecord>();
                }

                List<AlarmHistoryRecord> result = new List<AlarmHistoryRecord>();
                using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    ValidateHeader(reader.ReadLine(), filePath);
                    string line;
                    int lineNumber = 1;
                    while ((line = reader.ReadLine()) != null)
                    {
                        lineNumber++;
                        if (line.Length == 0)
                        {
                            continue;
                        }
                        List<string> fields = ParseCsvLine(line, filePath, lineNumber);
                        if (fields.Count != 7)
                        {
                            throw new InvalidDataException($"报警历史文件列数无效:{filePath}，第 {lineNumber} 行。");
                        }
                        result.Add(new AlarmHistoryRecord
                        {
                            AlarmCode = fields[0],
                            AlarmContent = fields[1],
                            AlarmCategory = fields[2],
                            StartTime = fields[3],
                            EndTime = fields[4],
                            Duration = fields[5],
                            Location = fields[6]
                        });
                    }
                }
                return result;
            }

            private static void ValidateHeader(string header, string filePath)
            {
                const string expected = "报警代码,报警内容,报警类别,开始时间,结束时间,报警时间(s),报警位置(x-x-x)";
                if (!string.Equals(header, expected, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("报警历史文件表头无效:" + filePath);
                }
            }

            private static List<string> ParseCsvLine(string line, string filePath, int lineNumber)
            {
                List<string> fields = new List<string>();
                StringBuilder value = new StringBuilder();
                bool quoted = false;
                for (int i = 0; i < line.Length; i++)
                {
                    char current = line[i];
                    if (quoted)
                    {
                        if (current == '"')
                        {
                            if (i + 1 < line.Length && line[i + 1] == '"')
                            {
                                value.Append('"');
                                i++;
                            }
                            else
                            {
                                quoted = false;
                            }
                        }
                        else
                        {
                            value.Append(current);
                        }
                        continue;
                    }

                    if (current == ',')
                    {
                        fields.Add(value.ToString());
                        value.Clear();
                    }
                    else if (current == '"')
                    {
                        if (value.Length != 0)
                        {
                            throw new InvalidDataException($"报警历史文件格式无效:{filePath}，第 {lineNumber} 行。");
                        }
                        quoted = true;
                    }
                    else
                    {
                        value.Append(current);
                    }
                }
                if (quoted)
                {
                    throw new InvalidDataException($"报警历史文件引号未闭合:{filePath}，第 {lineNumber} 行。");
                }
                fields.Add(value.ToString());
                return fields;
            }
        }
    }
}
