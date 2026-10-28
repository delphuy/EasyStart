using System;
using System.Reflection;
using Hardcodet.Wpf.TaskbarNotification;
class P {
  static void Main(){
    var t = typeof(TaskbarIcon);
    var p = t.GetProperty("Icon");
    Console.WriteLine("Icon type: " + p.PropertyType.FullName);
    Console.WriteLine("Icon assembly: " + p.PropertyType.Assembly.FullName);
  }
}
