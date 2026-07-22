using System;
// 模块：持久化 / 设备配置。
// 职责范围：管理控制卡、通讯、PLC、IO、工站和点位配置，不执行设备动作。

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;

namespace Automation
{
    public class CardConfigStore
    {
        private Card cardData = new Card();

        public Card CardData => cardData;

        public bool Load(string configPath, out string error)
        {
            error = null;
            if (!Directory.Exists(configPath))
            {
                Directory.CreateDirectory(configPath);
            }
            string filePath = Path.Combine(configPath, "card.json");
            if (!File.Exists(filePath))
            {
                cardData = Normalize(null);
                return Save(configPath, true, out error);
            }

            try
            {
                Card temp = AtomicJsonFileStore.Read<Card>(configPath, "card");
                if (temp == null)
                {
                    throw new InvalidDataException("控制卡主配置及备份均无法读取。");
                }
                cardData = Normalize(temp);
                if (!TryValidateAllAxes(out List<string> errors))
                {
                    error = "轴配置校验失败：" + string.Join("；", errors);
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                cardData = Normalize(null);
                return false;
            }
        }

        public bool Save(string configPath, bool validate, out string error)
        {
            error = null;
            if (!Directory.Exists(configPath))
            {
                Directory.CreateDirectory(configPath);
            }
            cardData = Normalize(cardData);
            if (validate && !TryValidateAllAxes(out List<string> errors))
            {
                error = "轴配置校验失败：" + string.Join("；", errors);
                return false;
            }
            if (AtomicJsonFileStore.Save(configPath, "card", cardData))
            {
                return true;
            }
            error = "控制卡配置保存失败。";
            return false;
        }

        public bool TryValidateAllAxes(out List<string> errors)
        {
            errors = new List<string>();
            if (cardData?.controlCards == null)
            {
                errors.Add("控制卡列表为空。");
                return false;
            }
            for (int cardIndex = 0; cardIndex < cardData.controlCards.Count; cardIndex++)
            {
                ControlCard controlCard = cardData.controlCards[cardIndex];
                if (controlCard?.cardHead == null || controlCard.axis == null)
                {
                    errors.Add($"{cardIndex}号卡配置为空。");
                    continue;
                }
                if (controlCard.cardHead.AxisCount != controlCard.axis.Count)
                {
                    errors.Add($"{cardIndex}号卡轴数量与轴列表不一致。");
                }
                HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
                for (int axisIndex = 0; axisIndex < controlCard.axis.Count; axisIndex++)
                {
                    Axis axis = controlCard.axis[axisIndex];
                    if (!TryValidateAxis(cardIndex, axisIndex, axis, out string error))
                    {
                        errors.Add(error);
                        continue;
                    }
                    if (!names.Add(axis.AxisName))
                    {
                        errors.Add($"{cardIndex}号卡轴名称重复:{axis.AxisName}");
                    }
                }
            }
            return errors.Count == 0;
        }

        public bool TryValidateAxis(int cardIndex, int axisIndex, Axis axis, out string error)
        {
            error = null;
            string prefix = $"{cardIndex}号卡{axisIndex}号轴";
            if (axis == null)
            {
                error = prefix + "配置为空。";
                return false;
            }
            if (axis.AxisNum != axisIndex)
            {
                error = $"{prefix}的轴号配置错误:{axis.AxisNum}";
                return false;
            }
            if (string.IsNullOrWhiteSpace(axis.AxisName))
            {
                error = prefix + "名称为空。";
                return false;
            }
            if (axis.PulseToMM <= 0 || axis.SpeedMax <= 0 || axis.AccMax <= 0 || axis.DecMax <= 0)
            {
                error = prefix + "脉冲当量、最大速度或加减速参数必须大于0。";
                return false;
            }
            if (!TryParsePositive(axis.HomeSpeed, out _))
            {
                error = prefix + "回原速度无效。";
                return false;
            }
            if (axis.HomeDirection != "正向" && axis.HomeDirection != "负向")
            {
                error = $"{prefix}回原搜索方向无效:{axis.HomeDirection}";
                return false;
            }
            return true;
        }

        public bool TryValidateStations(IEnumerable<DataStation> stations, out List<string> errors)
        {
            errors = new List<string>();
            if (stations == null)
            {
                errors.Add("工站列表为空。");
                return false;
            }
            HashSet<string> stationNames = new HashSet<string>(StringComparer.Ordinal);
            int stationIndex = -1;
            foreach (DataStation station in stations)
            {
                stationIndex++;
                if (station == null || string.IsNullOrWhiteSpace(station.Name))
                {
                    errors.Add($"{stationIndex}号工站为空或名称为空。");
                    continue;
                }
                if (!stationNames.Add(station.Name))
                {
                    errors.Add($"工站名称重复:{station.Name}");
                }
                if (station.dataAxis == null || station.homeSeq == null)
                {
                    errors.Add($"工站{station.Name}轴配置或回原顺序为空。");
                    continue;
                }
                AxisConfig[] configs =
                {
                    station.dataAxis?.axisConfig1, station.dataAxis?.axisConfig2, station.dataAxis?.axisConfig3,
                    station.dataAxis?.axisConfig4, station.dataAxis?.axisConfig5, station.dataAxis?.axisConfig6
                };
                if (station.dataAxis.axisConfigs == null)
                {
                    station.dataAxis.axisConfigs = new List<AxisConfig>();
                }
                station.dataAxis.axisConfigs.Clear();
                station.dataAxis.axisConfigs.AddRange(configs);
                HashSet<string> configuredNames = new HashSet<string>(StringComparer.Ordinal);
                HashSet<long> physicalAxes = new HashSet<long>();
                foreach (AxisConfig config in configs)
                {
                    if (config == null || config.AxisName == "-1")
                    {
                        continue;
                    }
                    if (!int.TryParse(config.CardNum, NumberStyles.None, CultureInfo.InvariantCulture, out int cardIndex)
                        || !TryGetAxisByName(cardIndex, config.AxisName, out Axis resolvedAxis))
                    {
                        errors.Add($"工站{station.Name}轴配置不存在:{config.CardNum}-{config.AxisName}");
                        continue;
                    }
                    long key = ((long)cardIndex << 32) | (uint)resolvedAxis.AxisNum;
                    if (!physicalAxes.Add(key))
                    {
                        errors.Add($"工站{station.Name}重复配置同一物理轴:{config.CardNum}-{config.AxisName}");
                    }
                    configuredNames.Add(config.AxisName);
                    config.axis = resolvedAxis;
                }
                AxisName[] sequence =
                {
                    station.homeSeq?.AxisName1, station.homeSeq?.AxisName2, station.homeSeq?.AxisName3,
                    station.homeSeq?.AxisName4, station.homeSeq?.AxisName5, station.homeSeq?.AxisName6
                };
                if (station.homeSeq.axisSeq == null)
                {
                    station.homeSeq.axisSeq = new List<AxisName>();
                }
                station.homeSeq.axisSeq.Clear();
                station.homeSeq.axisSeq.AddRange(sequence);
                HashSet<string> sequenceNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (AxisName item in sequence)
                {
                    if (item == null || item.Name == "-1")
                    {
                        continue;
                    }
                    if (!configuredNames.Contains(item.Name))
                    {
                        errors.Add($"工站{station.Name}回原顺序引用了未配置轴:{item.Name}");
                    }
                    else if (!sequenceNames.Add(item.Name))
                    {
                        errors.Add($"工站{station.Name}回原顺序轴重复:{item.Name}");
                    }
                }
                if (configuredNames.Count == 0)
                {
                    errors.Add($"工站{station.Name}没有配置任何轴。");
                }
            }
            return errors.Count == 0;
        }

        private static bool TryParsePositive(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public void SetCard(Card card)
        {
            cardData = Normalize(card);
        }

        public int AddControlCard(ControlCard controlCard)
        {
            if (cardData == null)
            {
                cardData = new Card();
            }
            if (cardData.controlCards == null)
            {
                cardData.controlCards = new List<ControlCard>();
            }
            if (controlCard == null)
            {
                return -1;
            }
            if (controlCard.cardHead == null)
            {
                controlCard.cardHead = new CardHead();
            }
            if (controlCard.axis == null)
            {
                controlCard.axis = new List<Axis>();
            }
            cardData.controlCards.Add(controlCard);
            return cardData.controlCards.Count - 1;
        }

        public bool RemoveControlCardAt(int cardIndex)
        {
            if (cardData == null || cardData.controlCards == null)
            {
                return false;
            }
            if (cardIndex < 0 || cardIndex >= cardData.controlCards.Count)
            {
                return false;
            }
            cardData.controlCards.RemoveAt(cardIndex);
            return true;
        }

        public int GetControlCardCount()
        {
            if (cardData == null || cardData.controlCards == null)
            {
                return 0;
            }
            return cardData.controlCards.Count;
        }

        public bool TryGetControlCard(int cardIndex, out ControlCard controlCard)
        {
            controlCard = null;
            if (cardData == null || cardData.controlCards == null)
            {
                return false;
            }
            if (cardIndex < 0 || cardIndex >= cardData.controlCards.Count)
            {
                return false;
            }
            controlCard = cardData.controlCards[cardIndex];
            return controlCard != null;
        }

        public bool TryGetCardHead(int cardIndex, out CardHead cardHead)
        {
            cardHead = null;
            if (!TryGetControlCard(cardIndex, out ControlCard controlCard))
            {
                return false;
            }
            cardHead = controlCard.cardHead;
            return cardHead != null;
        }

        public int GetAxisCount(int cardIndex)
        {
            if (!TryGetControlCard(cardIndex, out ControlCard controlCard))
            {
                return 0;
            }
            if (controlCard.axis == null)
            {
                return 0;
            }
            return controlCard.axis.Count;
        }

        public bool TryGetAxis(int cardIndex, int axisIndex, out Axis axis)
        {
            axis = null;
            if (!TryGetControlCard(cardIndex, out ControlCard controlCard))
            {
                return false;
            }
            if (controlCard.axis == null || axisIndex < 0 || axisIndex >= controlCard.axis.Count)
            {
                return false;
            }
            axis = controlCard.axis[axisIndex];
            return axis != null;
        }

        public void ReplaceControlCard(int cardIndex, ControlCard controlCard)
        {
            if (controlCard == null)
            {
                throw new ArgumentNullException(nameof(controlCard));
            }
            if (cardData?.controlCards == null || cardIndex < 0 || cardIndex >= cardData.controlCards.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(cardIndex));
            }
            cardData.controlCards[cardIndex] = controlCard;
        }

        public void ReplaceAxis(int cardIndex, int axisIndex, Axis axis)
        {
            if (axis == null)
            {
                throw new ArgumentNullException(nameof(axis));
            }
            if (!TryGetControlCard(cardIndex, out ControlCard controlCard)
                || controlCard.axis == null || axisIndex < 0 || axisIndex >= controlCard.axis.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(axisIndex));
            }
            controlCard.axis[axisIndex] = axis;
        }

        public bool TryGetAxisByName(int cardIndex, string axisName, out Axis axis)
        {
            axis = null;
            if (string.IsNullOrWhiteSpace(axisName))
            {
                return false;
            }
            if (!TryGetControlCard(cardIndex, out ControlCard controlCard))
            {
                return false;
            }
            if (controlCard.axis == null)
            {
                return false;
            }
            axis = controlCard.axis.FirstOrDefault(item => item != null && item.AxisName == axisName);
            return axis != null;
        }

        private Card Normalize(Card card)
        {
            Card result = card ?? new Card();
            if (result.controlCards == null)
            {
                result.controlCards = new List<ControlCard>();
            }
            foreach (ControlCard controlCard in result.controlCards)
            {
                if (controlCard == null)
                {
                    continue;
                }
                if (controlCard.cardHead == null)
                {
                    controlCard.cardHead = new CardHead();
                }
                if (controlCard.axis == null)
                {
                    controlCard.axis = new List<Axis>();
                }
            }
            return result;
        }
    }
}
