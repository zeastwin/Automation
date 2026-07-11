using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
                    TypeNameHandling = TypeNameHandling.None,
                    ObjectCreationHandling = ObjectCreationHandling.Replace
                };
                JToken root = JToken.Parse(json);
                // 兼容旧版 TypeNameHandling.All 生成的 {$type,$values} 外壳，但绝不解析其中的类型名。
                JToken values = root.Type == JTokenType.Object && root["$values"] is JArray
                    ? root["$values"]
                    : root;
                List<AlarmInfo> temp = values.ToObject<List<AlarmInfo>>(JsonSerializer.Create(settings));
                LoadFromList(temp);
                return true;
            }
            catch (Exception ex)
            {
                ResetAlarms();
                SF.SetSecurityLock($"报警配置加载失败:{ex.Message}");
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
                TypeNameHandling = TypeNameHandling.None
            };
            List<AlarmInfo> snapshot;
            lock (alarmLock)
            {
                snapshot = alarms.Select(CloneAlarm).ToList();
            }
            ValidateSnapshot(snapshot);
            string output = JsonConvert.SerializeObject(snapshot, settings);
            WriteAllTextAtomic(filePath, output);
            return true;
        }

        public void UpdateAlarm(int index, string name, string category, string btn1, string btn2,
            string btn3, string note)
        {
            if (index < 0 || index >= AlarmCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            var candidate = new AlarmInfo
            {
                Index = index,
                Name = Normalize(name),
                Category = Normalize(category),
                Btn1 = Normalize(btn1),
                Btn2 = Normalize(btn2),
                Btn3 = Normalize(btn3),
                Note = Normalize(note)
            };
            ValidateAlarm(candidate);
            lock (alarmLock)
            {
                AlarmInfo target = alarms[index];
                target.Name = candidate.Name;
                target.Category = candidate.Category;
                target.Btn1 = candidate.Btn1;
                target.Btn2 = candidate.Btn2;
                target.Btn3 = candidate.Btn3;
                target.Note = candidate.Note;
            }
        }

        public void ClearAlarm(int index)
        {
            UpdateAlarm(index, null, null, null, null, null, null);
        }

        private static void ValidateSnapshot(IReadOnlyList<AlarmInfo> snapshot)
        {
            if (snapshot == null || snapshot.Count != AlarmCapacity)
            {
                throw new InvalidDataException($"报警信息数量必须为 {AlarmCapacity}。" );
            }
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i] == null || snapshot[i].Index != i)
                {
                    throw new InvalidDataException($"报警信息编号不连续，位置:{i}。" );
                }
                ValidateAlarm(snapshot[i]);
            }
        }

        private static void ValidateAlarm(AlarmInfo alarm)
        {
            bool hasName = !string.IsNullOrWhiteSpace(alarm.Name);
            bool hasNote = !string.IsNullOrWhiteSpace(alarm.Note);
            if (hasName != hasNote)
            {
                throw new InvalidDataException($"报警[{alarm.Index}]名称与报警信息必须同时填写。" );
            }
            if (!hasName && (!string.IsNullOrWhiteSpace(alarm.Category)
                || !string.IsNullOrWhiteSpace(alarm.Btn1) || !string.IsNullOrWhiteSpace(alarm.Btn2)
                || !string.IsNullOrWhiteSpace(alarm.Btn3)))
            {
                throw new InvalidDataException($"报警[{alarm.Index}]为空槽位时不能保留分类或按钮文本。" );
            }
        }

        private static string Normalize(string value)
        {
            string normalized = value?.Trim();
            return string.IsNullOrEmpty(normalized) ? null : normalized;
        }

        private static AlarmInfo CloneAlarm(AlarmInfo source)
        {
            return new AlarmInfo
            {
                Index = source.Index,
                Name = source.Name,
                Category = source.Category,
                Btn1 = source.Btn1,
                Btn2 = source.Btn2,
                Btn3 = source.Btn3,
                Note = source.Note
            };
        }

        private static void WriteAllTextAtomic(string filePath, string content)
        {
            string directory = Path.GetDirectoryName(filePath);
            string tempPath = Path.Combine(directory,
                Path.GetFileName(filePath) + ".tmp." + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllText(tempPath, content);
                if (File.Exists(filePath))
                {
                    File.Replace(tempPath, filePath, null);
                }
                else
                {
                    File.Move(tempPath, filePath);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
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
            ValidateSnapshot(source);
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
