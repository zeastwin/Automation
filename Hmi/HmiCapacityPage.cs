using System.Windows.Forms;

namespace Automation.Hmi
{
    public partial class HmiCapacityPage : Form
    {
        private ProductionStatusCard productionStatusCard;

        public HmiCapacityPage()
        {
            InitializeComponent();
            InitializeProductionStatusCard();
        }

        private void InitializeProductionStatusCard()
        {
            productionStatusCard = new ProductionStatusCard
            {
                DailyTarget = 1000,
                CurrentOutput = 0,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 8)
            };

            this.Controls.Add(productionStatusCard);
            productionStatusCard.BringToFront();
        }
    }
}
