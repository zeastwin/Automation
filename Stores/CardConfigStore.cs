using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json;
using static Automation.FrmCard;

namespace Automation
{
    public class CardConfigStore
    {
        private Card cardData = new Card();

        public Card CardData => cardData;

        public bool Load(string configPath)
        {
            if (!Directory.Exists(configPath))
            {
                Directory.CreateDirectory(configPath);
            }
            string filePath = Path.Combine(configPath, "card.json");
            if (!File.Exists(filePath))
            {
                cardData = Normalize(null);
                return false;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                var settings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.All
                };
                Card temp = JsonConvert.DeserializeObject<Card>(json, settings);
                cardData = Normalize(temp);
                return true;
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
                cardData = Normalize(null);
                return false;
            }
        }

        public bool Save(string configPath)
        {
            if (!Directory.Exists(configPath))
            {
                Directory.CreateDirectory(configPath);
            }
            cardData = Normalize(cardData);
            string filePath = Path.Combine(configPath, "card.json");
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All
            };
            string output = JsonConvert.SerializeObject(cardData, settings);
            File.WriteAllText(filePath, output);
            return true;
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
