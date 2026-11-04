using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Renders the production WPF card using synthetic data only. No window is shown.
class RenderPreview {
    [STAThread] static int Main(string[] args) {
        try {
            var assembly=Assembly.LoadFrom(Path.GetFullPath(args[0]));
            var type=assembly.GetType("CodexQuotaFloat.QuotaFloat",true);
            var localization=assembly.GetType("CodexQuotaFloat.L10n",true);
            foreach(string language in new[]{"en","zh-CN"}) {
                localization.GetField("Language").SetValue(null,language);
                var window=(Window)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{true,false,true},null);
                var sample=type.GetMethod("Sample",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
                type.GetField("snapshot",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(window,sample);
                type.GetMethod("Render",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly).Invoke(window,null);
                var card=(FrameworkElement)window.Content;window.Content=null;
                card.Measure(new Size(312,76));card.Arrange(new Rect(0,0,312,76));card.UpdateLayout();
                CheckTextFits(card);
                var bitmap=new RenderTargetBitmap(936,228,288,288,PixelFormats.Pbgra32);bitmap.Render(card);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                Directory.CreateDirectory(args[1]);
                using(var output=File.Create(Path.Combine(args[1],"preview-"+language+".png")))encoder.Save(output);
                window.Close();
                Console.WriteLine("PASS: "+language+" synthetic production-card render; text fits.");
            }
            return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    static void CheckTextFits(DependencyObject node) {
        var text=node as TextBlock;
        if(text!=null && text.DesiredSize.Width>text.ActualWidth+.5)throw new InvalidOperationException("Text is clipped: "+text.Text);
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)CheckTextFits(VisualTreeHelper.GetChild(node,i));
    }
}
