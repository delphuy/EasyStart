// 独立验证：SettingsWindow 能否正常创建+Show（STA 线程）
using System.Windows;
using System.Threading;
using EasyStart.Panel;
using EasyStart.Storage;

var ctx = EasyStart.AppContext.Init("test", onChanged: () => { });
string result = "OK: 未进 STA";
var sta = new Thread(() =>
{
    try
    {
        var host = new Window { Title = "host", Width = 10, Height = 10, ShowInTaskbar = false };
        host.Show();
        host.Hide();
        SettingsWindow.Show(ctx.SettingsService);
        result = "OK: SettingsWindow.Show 调用成功，无异常";
        System.Windows.Forms.Application.DoEvents();
    }
    catch (Exception ex)
    {
        result = "FAIL: " + ex.Message + "\n" + ex.StackTrace;
    }
});
sta.SetApartmentState(ApartmentState.STA);
sta.Start();
sta.Join();
Console.WriteLine(result);
Console.WriteLine("测试完成");
