using Automation.MotionControl;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using S7.Net;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmTest : Form
    {
        BarSeries s1;
        public FrmTest()
        {
            InitializeComponent();


            var model = new PlotModel
            {
                Title = "灌胶段1胶阀压力",
            };

            var l = new Legend
            {
                LegendPlacement = LegendPlacement.Outside,
                LegendPosition = LegendPosition.BottomCenter,
                LegendOrientation = LegendOrientation.Horizontal,
                LegendBorderThickness = 0
            };
            model.Legends.Add(l);

            s1 = new BarSeries
            {
                IsStacked = false,
                StrokeColor = OxyColors.Black,
                StrokeThickness = 1,
                XAxisKey = "x",
                YAxisKey = "y",
                FillColor = OxyColors.LightBlue,
                LabelPlacement = LabelPlacement.Inside,
                LabelFormatString = "{0}",

            };
            s1.Items.Add(new BarItem { Value = 0 });
            s1.Items.Add(new BarItem { Value = 0 });
            s1.Items.Add(new BarItem { Value = 0 });
            s1.Items.Add(new BarItem { Value = 0 });

            var categoryAxis = new CategoryAxis { Position = AxisPosition.Bottom, Key = "y" };
            categoryAxis.Labels.Add("胶阀1");
            categoryAxis.Labels.Add("胶阀2");
            categoryAxis.Labels.Add("胶阀3");
            categoryAxis.Labels.Add("胶阀4");
            var valueAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                MinimumPadding = 0,
                MaximumPadding = 0.06,
                AbsoluteMinimum = 0,
                Key = "x",
                Maximum = 100,
                Minimum = 0,
                //Title = "气压",
                //TitlePosition = 0.6,

            };
            model.Series.Add(s1);
            model.Axes.Add(categoryAxis);
            model.Axes.Add(valueAxis);

            plotView1.Model = model;
        }

        public class DataVariable
        {
            public string Name { get; set; }
            public double Value { get; set; }
        }
        private List<List<double>> dataSources = new List<List<double>>();
        private void FrmTest_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        private void FrmTest_Load(object sender, EventArgs e)
        {

            //timer1.Interval = 300; // 设置定时器的间隔，单位毫秒
            //timer1.Start();


        }

        private void button1_Click(object sender, EventArgs e)
        {
            // PLC连接配置
            var plc = new Plc(CpuType.S7200, "192.168.0.1", 0, 1);

            try
            {
                // 打开PLC连接
                plc.Open();

                // 读取DB数据块
                int dbNumber = 1; // 设置DB号
                int startByte = 0; // 设置起始字节
                int length = 10; // 设置读取的字节长度

                byte[] data = plc.ReadBytes(DataType.DataBlock, dbNumber, startByte, length);

                // 输出读取的数据
                Console.WriteLine("Read data from DB{0}, Start Byte: {1}, Length: {2}", dbNumber, startByte, length);
                Console.WriteLine("Data: " + BitConverter.ToString(data).Replace("-", " "));

            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
            finally
            {
                // 关闭PLC连接
                if (plc.IsConnected)
                    plc.Close();
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            //SF.motion.Jog(0,0,1);
            SF.motion.Mov(0, 0, 10, 0,false);
        }

        private void button3_Click(object sender, EventArgs e)
        {
            //SF.motion.StopOneAxis(0,0);
        }

        private void button4_Click(object sender, EventArgs e)
        {
            double ss = -1;
            csLTDMC.LTDMC.dmc_get_equiv(0,0,ref ss);
            Console.WriteLine(ss);
        }

        private void button5_Click(object sender, EventArgs e)
        {
            //    csLTDMC.LTDMC.dmc_set_equiv(0, 0, 1000);
            SF.frmInfo.PrintInfo("", FrmInfo.Level.Error);
        }

        private void button6_Click(object sender, EventArgs e)
        {
            // Console.WriteLine(SF.motion.HomeStatus(0));
            //uint number = csLTDMC.LTDMC.dmc_read_outport(0, 0);
            uint number = csLTDMC.LTDMC.dmc_axis_io_status(0, 0);

            string binaryString = Convert.ToString(number, 2).PadLeft(16, '0');

            Console.WriteLine(binaryString);
        }

        private void button7_Click(object sender, EventArgs e)
        {
            //SF.motion.SettHomeParam(0,0,5000,);
            Random random = new Random();
            for (int i = 0; i < 10; i++)
            {
               
            }
        }
        public async Task Start(int i,Random random)
        {
          
            int randomNumber = (int)((random.NextDouble() * (2 - 1) + 1)*1000); // 生成1到2之间的随机数
            Console.WriteLine(randomNumber);
            await Task.Delay(randomNumber*5);
            Console.WriteLine(i+":Complete:"+randomNumber);
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            // 模拟实时数据更新
            Random random = new Random();

      

            for (int i = 0; i < 4; i++)
            {
                double newValue = random.Next(0, 100);
                s1.Items[i].Value = newValue;
            }
            plotView1.InvalidatePlot(true);
         

        }
    }
}
