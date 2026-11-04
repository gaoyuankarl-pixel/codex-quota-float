using System;
using System.Windows;
using System.Windows.Media;

namespace CodexQuotaFloat {
static class StripLayout {
    public const double Width=312,Height=76;
    public const int Revision=1;
    public static void Migrate(Preferences preferences) {
        if(preferences.LayoutRevision>=Revision)return;
        preferences.Left=double.NaN;preferences.Top=double.NaN;preferences.LayoutRevision=Revision;
    }
    public static Point Dock(Rect workArea,double width,double height) {
        return new Point(Math.Max(workArea.Left,workArea.Right-width),Math.Max(workArea.Top,workArea.Bottom-height));
    }
}
sealed class QuotaRing : FrameworkElement {
    double? remaining;
    Brush color=Brushes.Gray;
    static readonly Pen Track=new Pen(new SolidColorBrush(Color.FromRgb(58,65,69)),4);
    public void Set(double? value,Brush brush) {
        remaining=value.HasValue?(double?)Math.Max(0,Math.Min(100,value.Value)):null;color=brush;InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc) {
        base.OnRender(dc);
        var center=new Point(ActualWidth/2,ActualHeight/2);double radius=Math.Max(0,Math.Min(ActualWidth,ActualHeight)/2-3);
        dc.DrawEllipse(null,Track,center,radius,radius);
        if(!remaining.HasValue||remaining.Value<=0||radius<=0)return;
        var pen=new Pen(color,4){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
        if(remaining.Value>=100){dc.DrawEllipse(null,pen,center,radius,radius);return;}
        // The consumed gap advances clockwise from 12 o'clock as remaining quota falls.
        double consumedAngle=(100-remaining.Value)/100*2*Math.PI;
        var start=new Point(center.X+radius*Math.Sin(consumedAngle),center.Y-radius*Math.Cos(consumedAngle));
        var geometry=new StreamGeometry();
        using(var context=geometry.Open()) {
            context.BeginFigure(start,false,false);
            context.ArcTo(new Point(center.X,center.Y-radius),new Size(radius,radius),0,remaining.Value>50,SweepDirection.Clockwise,true,false);
        }
        geometry.Freeze();dc.DrawGeometry(null,pen,geometry);
    }
}
}
