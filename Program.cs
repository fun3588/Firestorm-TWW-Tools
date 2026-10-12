using System;
using System.Windows.Forms;
using FirestormTwwMonitor.UI;

namespace FirestormTwwMonitor
{
    internal static class Program
    {
        /// <summary>
        /// 应用程序主入口点。
        /// </summary>
        [STAThread]
        private static void Main()
        {
            try
            {
                ApplicationConfiguration.Initialize();
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show("启动异常: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
