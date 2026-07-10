using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace Automation
{
    public class AlarmInfoStore
    {
        public const int AlarmCapacity = 1000;

        private readonly object alarmLock = new object();
        private readonly BindingList<AlarmInfo> alarms = new BindingList<AlarmInfo>();

        public BindingList<AlarmInfo> Alarms => alarms;

        public AlarmInfoStore()
        {
            ResetAlarms();
        }

        public bool Load(string configPath)
        {
            if (!Directory.Exists(configPath))
            {
                Directory.CreateDirectory(configPath);
            }
            string filePath = Path.Combine(configPath, "AlarmInfo.json");
            if (!File.Exists(filePath))
            {
                ResetAlarms();
                Save(configPath);
                return false;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                var settings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.All,
                    ObjectCreationHandling = ObjectCreationHandling.Replace
                };
                List<AlarmInfo> temp = JsonConvert.DeserializeObject<List<AlarmInfo>>(json, settings);
                LoadFromList(temp);
                return true;
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
                ResetAlarms();
                return false;
            }
        }

        public bool Save(string configPath)
        {
            if (!Directory.Exists(configPath))
            {
                Directory.CreateDirectory(configPath);
            }
            string filePath = Path.Combine(configPath, "AlarmInfo.json");
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All
            };
            List<AlarmInfo> snapshot;
            lock (alarmLock)
            {
                snapshot = alarms.ToList();
            }
            string output = JsonConvert.SerializeObject(snapshot, settings);
            File.WriteAllText(filePath, output);
            return true;
        }

        private void ResetAlarms()
        {
            alarms.RaiseListChangedEvents = false;
            alarms.Clear();
            for (int i = 0; i < AlarmCapacity; i++)
            {
                alarms.Add(new AlarmInfo { Index = i });
            }
            alarms.RaiseListChangedEvents = true;
        }

        private void LoadFromList(List<AlarmInfo> source)
        {
            if (source == null)
            {
                throw new InvalidDataException("报警信息配置为空。");
            }
            if (source.Count != AlarmCapacity)
            {
                throw new InvalidDataException($"报警信息数量必须为 {AlarmCapacity}。");
            }
            for (int i = 0; i < AlarmCapacity; i++)
            {
                AlarmInfo item = source[i];
                if (item == null)
                {
                    throw new InvalidDataException($"报警信息[{i}]为空。");
                }
                if (item.Index != i)
                {
                    throw new InvalidDataException($"报警信息编号不匹配，当前:{item.Index}，期望:{i}。");
                }
            }
            lock (alarmLock)
            {
                ResetAlarms();
                for (int i = 0; i < AlarmCapacity; i++)
                {
                    AlarmInfo item = source[i];
                    AlarmInfo target = alarms[i];
                    target.Index = i;
                    target.Name = item.Name;
                    target.Category = item.Category;
                    target.Btn1 = item.Btn1;
                    target.Btn2 = item.Btn2;
                    target.Btn3 = item.Btn3;
                    target.Note = item.Note;
                }
            }
        }

        public bool TryGetByIndex(int index, out AlarmInfo alarm)
        {
            alarm = null;
            if (index < 0 || index >= AlarmCapacity)
            {
                return false;
            }
            lock (alarmLock)
            {
                alarm = alarms[index];
                return alarm != null;
            }
        }

        public List<int> GetValidIndices()
        {
            lock (alarmLock)
            {
                return alarms.Where(item => !string.IsNullOrEmpty(item.Name))
                    .Select(item => item.Index)
                    .ToList();
            }
        }
    }

    public class AlarmInfo
    {
        public int Index { get; set; }

        public string Name { get; set; }

        public string Category { get; set; }

        public string Btn1 { get; set; }

        public string Btn2 { get; set; }

        public string Btn3 { get; set; }

        public string Note { get; set; }
    }
}
