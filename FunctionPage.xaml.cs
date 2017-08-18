using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO.Ports;
using System.IO;
using System.Windows.Threading;
using System.Xml.Serialization;
using CMLCOMLib;
using System.Diagnostics;

namespace PVTforOneMotor
{
    /// <summary>
    /// FunctionPage.xaml 的交互逻辑
    /// </summary>
    public partial class FunctionPage : Page
    {
        public static class common // F-控制电机代号（0-3）
        {
            public static int NUM = 2;          
        }

        public FunctionPage()
        {
            InitializeComponent();
        }
        private AmpObj[] ampObj; //声明驱动器
        private ProfileSettingsObj profileSettingsObj; //声明驱动器属性
        private canOpenObj canObj; //声明网络接口

        private DispatcherTimer ShowCurTimeTimer; //显示当前时间的计时器
        //private DispatcherTimer ShowTextTimer; //输出电机参数的计时器
        //private DispatcherTimer WriteDataTimer; //写入数据委托的计时器
        private DispatcherTimer GaitStartTimer;


        #region 当前时间显示
        public void ShowCurTimer(object sender, EventArgs e)//取当前时间的委托
        {
            string timeDateString = "";
            DateTime now = DateTime.Now;
            timeDateString = string.Format("{0}年{1}月{2}日 {3}:{4}:{5}",
                now.Year,
                now.Month.ToString("00"),
                now.Day.ToString("00"),
                now.Hour.ToString("00"),
                now.Minute.ToString("00"),
                now.Second.ToString("00"));

            timeDateTextBlock.Text = timeDateString;

        }
        #endregion

        private void FunctionPage_Loaded(object sender, RoutedEventArgs e)//打开窗口后进行的初始化操作
        {
            //显示当前时间的计时器
            ShowCurTimeTimer = new DispatcherTimer();
            ShowCurTimeTimer.Tick += new EventHandler(ShowCurTimer);
            ShowCurTimeTimer.Interval = new TimeSpan(0, 0, 0, 1, 0);
            ShowCurTimeTimer.Start();

            try
            {
                canObj = new canOpenObj();//实例化网络接口
                canObj = new canOpenObj(); //实例化网络接口
                profileSettingsObj = new ProfileSettingsObj(); //实例化驱动器属性

                canObj.BitRate = CML_BIT_RATES.BITRATE_1_Mbit_per_sec; //设置CAN传输速率为1M/s
                canObj.Initialize(); //网络接口初始化

                ampObj = new AmpObj[common.NUM]; //实例化单个驱动器（盘式电机）      
                ampObj[common.NUM] = new AmpObj();
                ampObj[common.NUM].Initialize(canObj, (short)(common.NUM + 1));
                ampObj[common.NUM].HaltMode = CML_HALT_MODE.HALT_DECEL; //选择通过减速来停止电机的方式

                //Linkage = new LinkageObj();//F-实例化联动
                //Linkage.Initialize(ampObj);//F-将初始化成功的对象粘连
                //EventObj = Linkage.CreateEvent(CML_LINK_EVENT.LINKEVENT_LOWWATER, CML_EVENT_CONDITION.CML_EVENT_ALL);//F-Linkage多驱动器的协同联动

                if (ampObj == null)
                {
                    statusBar.Background = new SolidColorBrush(Color.FromArgb(255, 230, 20, 20));
                    statusInfoTextBlock.Text = "电机初始化失败！";
                }
            }

            catch
            {
                statusBar.Background = new SolidColorBrush(Color.FromArgb(255, 230, 20, 20));
                statusInfoTextBlock.Text = "窗口初始化失败！";
            }
        }

        private double[] trajectory = new double[1100]; //步态采集数据轨迹
        private double[] trajectories = new double[1001]; //输入单个关节的步态数据
        private double[] tempPositionActual = new double[1001]; //记录步态进行时电机的实际位置
        private int[] countor = new int[1001];
        private double[] originalTrajectories = new double[1001]; //保存初始输入数据

        #region PVTButton
        private void startButton_Click(object sender, RoutedEventArgs e)
        {
            string[] rawData = File.ReadAllLines("C:\\Users\\Administrator\\Desktop\\ExoGaitControl\\GaitSequence.txt", Encoding.Default);
            int PVTlines = rawData.GetLength(0);
            double[] pos = new double[PVTlines];
            double[] vel = new double[PVTlines];
            int[] tim = new int[PVTlines];
            int t = 20;

            for (int i = 0; i < PVTlines; i++)
            {
                string[] str = (rawData[i] ?? string.Empty).Split(new char[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
                pos[i] = (int)(double.Parse(str[i]) * 6400);
                tim[i] = 20;
                if (i == 0)
                    vel[i] = 0;
                else
                    vel[i] = (pos[i] - pos[i - 1]) / t;
            }
            profileSettingsObj.ProfileType = CML_PROFILE_TYPE.PROFILE_VELOCITY;
            profileSettingsObj.ProfileAccel = (ampObj[common.NUM].VelocityLoopSettings.VelLoopMaxAcc) / 10; 
            profileSettingsObj.ProfileDecel = (ampObj[common.NUM].VelocityLoopSettings.VelLoopMaxDec) / 10;
            profileSettingsObj.ProfileVel = (ampObj[common.NUM].VelocityLoopSettings.VelLoopMaxVel) / 10;
            ampObj[common.NUM].ProfileSettings = profileSettingsObj;

            ampObj[common.NUM].MoveAbs(pos[0]);   // start y motor at midpoint
            ampObj[common.NUM].WaitMoveDone(4000);
           
            ampObj[common.NUM].PositionActual = 0;

            GaitStartTimer = new DispatcherTimer();
            GaitStartTimer.Tick += new EventHandler(gaitStartTimer);
            GaitStartTimer.Interval = TimeSpan.FromMilliseconds(20);// 该时钟频率决定电机运行速度
            GaitStartTimer.Start();
        }

        int timeCountor = 0; //记录录入数据个数的计数器
        public void gaitStartTimer(object sender, EventArgs e)//开始步态的委托
        {
            statusBar.Background = new SolidColorBrush(Color.FromArgb(255, 230, 20, 20));
            statusInfoTextBlock.Text = "正在执行";
            timeCountor++;
            tempPositionActual[timeCountor] = ampObj[common.NUM].PositionActual;

            if (timeCountor > 499)
            {
                statusBar.Background = new SolidColorBrush(Color.FromArgb(255, 0, 122, 204));
                statusInfoTextBlock.Text = "执行完毕";

                ampObj[common.NUM].HaltMove();
                GaitStartTimer.Stop();
            }

        }

        #endregion

        #region 停止按钮
        private void endButton_Click(object sender, RoutedEventArgs e)//点击【步态结束】按钮时执行
        {
            startButton.IsEnabled = true;
            endButton.IsEnabled = false;

            statusBar.Background = new SolidColorBrush(Color.FromArgb(255, 0, 122, 204));
            statusInfoTextBlock.Text = "执行完毕";

            ampObj[common.NUM].HaltMove();
            GaitStartTimer.Stop();
        }
        #endregion

        #region 执行按钮
        private DispatcherTimer AngleSetTimer; //电机按设置转角转动的计时器
        private void angleSetButton_Click(object sender, RoutedEventArgs e)
        {
            angleSetButton.IsEnabled = false;
            emergencyStopButton.IsEnabled = true;
            zeroPointSetButton.IsEnabled = true;
            int motorNumber = Convert.ToInt16(motorNumberTextBox.Text);
            int i = motorNumber - 1;

            ampObj[i].PositionActual = 0;

            AngleSetTimer = new DispatcherTimer();
            AngleSetTimer.Tick += new EventHandler(angleSetTimer);
            AngleSetTimer.Interval = TimeSpan.FromMilliseconds(20);// 该时钟频率决定电机运行速度
            AngleSetTimer.Start();
        }
        
        private double ampObjAngleActual = new double();//电机的转角
        public void angleSetTimer(object sender, EventArgs e)//电机按设置角度转动的委托
        {
            statusBar.Background = new SolidColorBrush(Color.FromArgb(255, 230, 20, 20));
            statusInfoTextBlock.Text = "正在执行";

            double angleSet = Convert.ToDouble(angleSetTextBox.Text);
       
            ampObjAngleActual = ampObj[common.NUM].PositionActual / 6400;


            if (angleSet > 0)
            {
                if (ampObjAngleActual < angleSet)
                {
                    profileSettingsObj.ProfileVel = 50000;
                    profileSettingsObj.ProfileAccel = 50000;
                    profileSettingsObj.ProfileDecel = profileSettingsObj.ProfileAccel;
                    ampObj[common.NUM].ProfileSettings = profileSettingsObj;

                    if (ampObjAngleActual > (angleSet - 5))
                    {
                        profileSettingsObj.ProfileVel = 25000;
                        profileSettingsObj.ProfileAccel = 25000;
                        profileSettingsObj.ProfileDecel = profileSettingsObj.ProfileAccel;
                        ampObj[common.NUM].ProfileSettings = profileSettingsObj;
                    }
                    ampObj[common.NUM].MoveRel(1);
                }
                else
                {
                    ampObj[common.NUM].HaltMove();
                    angleSetButton.IsEnabled = true;
                    AngleSetTimer.Stop();
                    statusBar.Background = new SolidColorBrush(Color.FromArgb(255, 0, 122, 204));
                    statusInfoTextBlock.Text = "执行完毕";
                }
            }

            if (angleSet < 0)
            {
                if (ampObjAngleActual > angleSet)
                {
                    profileSettingsObj.ProfileVel = 50000;
                    profileSettingsObj.ProfileAccel = 50000;
                    profileSettingsObj.ProfileDecel = profileSettingsObj.ProfileAccel;
                    ampObj[common.NUM].ProfileSettings = profileSettingsObj;

                    if (ampObjAngleActual < (angleSet + 5))
                    {
                        profileSettingsObj.ProfileVel = 25000;
                        profileSettingsObj.ProfileAccel = 25000;
                        profileSettingsObj.ProfileDecel = profileSettingsObj.ProfileAccel;
                        ampObj[common.NUM].ProfileSettings = profileSettingsObj;
                    }
                    ampObj[common.NUM].MoveRel(-1);
                }
                else
                {
                    ampObj[common.NUM].HaltMove();
                    angleSetButton.IsEnabled = true;
                    AngleSetTimer.Stop();
                    statusBar.Background = new SolidColorBrush(Color.FromArgb(255, 0, 122, 204));
                    statusInfoTextBlock.Text = "执行完毕";
                }
            }

        }
        #endregion

        #region 急停按钮
        private void emergencyStopButton_Click(object sender, RoutedEventArgs e)
        {
            emergencyStopButton.IsEnabled = false;
            angleSetButton.IsEnabled = true;
            
            ampObj[common.NUM].HaltMove();
            AngleSetTimer.Stop();

        }
        #endregion

        #region 设置零点按钮
        private void zeroPointSetButton_Click(object sender, RoutedEventArgs e)
        {
            ampObj[common.NUM].PositionActual = 0;            
        }
        #endregion

        #region 回归零点按钮
        private DispatcherTimer GetZeroPointTimer; //回归原点的计时器
        private void getZeroPointButton_Click(object sender, RoutedEventArgs e)
        {
            GetZeroPointTimer = new DispatcherTimer();
            GetZeroPointTimer.Tick += new EventHandler(getZeroPointTimer);
            GetZeroPointTimer.Interval = TimeSpan.FromMilliseconds(20);// 该时钟频率决定电机运行速度
            GetZeroPointTimer.Start();
        }

        public void getZeroPointTimer(object sender, EventArgs e)//回归原点的委托
        {
            statusBar.Background = new SolidColorBrush(Color.FromArgb(255, 230, 20, 20));
            statusInfoTextBlock.Text = "正在回归原点";
            ampObjAngleActual = ampObj[common.NUM].PositionActual / 6400;

            if (Math.Abs(ampObjAngleActual) > 3)//电机1回归原点
            {
                if (ampObjAngleActual > 0)//此时电机1应往后转
                {
                    if (ampObjAngleActual > 10)
                    {
                        profileSettingsObj.ProfileVel = 50000;
                        profileSettingsObj.ProfileAccel = 50000;
                        profileSettingsObj.ProfileDecel = profileSettingsObj.ProfileAccel;
                        ampObj[common.NUM].ProfileSettings = profileSettingsObj;
                    }
                    else
                    {
                        profileSettingsObj.ProfileVel = 25000;
                        profileSettingsObj.ProfileAccel = 25000;
                        profileSettingsObj.ProfileDecel = profileSettingsObj.ProfileAccel;
                        ampObj[common.NUM].ProfileSettings = profileSettingsObj;
                    }
                    ampObj[common.NUM].MoveRel(-1);
                }
                else//此时电机1应往前转
                {
                    if (ampObjAngleActual < -10)
                    {
                        profileSettingsObj.ProfileVel = 50000;
                        profileSettingsObj.ProfileAccel = 50000;
                        profileSettingsObj.ProfileDecel = profileSettingsObj.ProfileAccel;
                        ampObj[common.NUM].ProfileSettings = profileSettingsObj;
                    }
                    else
                    {
                        profileSettingsObj.ProfileVel = 25000;
                        profileSettingsObj.ProfileAccel = 25000;
                        profileSettingsObj.ProfileDecel = profileSettingsObj.ProfileAccel;
                        ampObj[common.NUM].ProfileSettings = profileSettingsObj;
                    }
                    ampObj[common.NUM].MoveRel(1);
                }
            }
            else
            {
                ampObj[common.NUM].HaltMove();
            }
            if (Math.Abs(ampObjAngleActual) < 3)
            {
                statusBar.Background = new SolidColorBrush(Color.FromArgb(255, 0, 122, 204));
                statusInfoTextBlock.Text = "回归原点完毕";
                GetZeroPointTimer.Stop();
            }
        }            
        #endregion

    }
}
