using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Input.Inking;

namespace StrokeSampler
{
    internal class DrawingHelpers
    {
        internal static void DrawPreviewLabels(MainPage mp, CanvasDrawingSession ds)
        {
            var format = new CanvasTextFormat
            {
                FontSize = 22,
                WordWrapping = CanvasWordWrapping.NoWrap
            };

            ds.DrawText("Tool=Pencil", 16, 16, Colors.Black, format);

            if (mp._lastWasDotGrid && mp._lastMaxOverwrite is int dotMax && mp._lastDotGridSpacing is int dotSpacing)
            {
                ds.DrawText("Mode=DotGrid", 16, 44, Colors.Black, format);
                ds.DrawText($"Pressure={string.Join("/", MainPage.DotGridPressurePreset)}", 16, 72, Colors.Black, format);
                ds.DrawText($"MaxOverwrite={dotMax}", 16, 100, Colors.Black, format);
                ds.DrawText($"DotSpacing={dotSpacing}", 16, 128, Colors.Black, format);
            }
            else if (mp._lastMaxOverwrite is int maxOverwrite && mp._lastOverwritePressure is float pressure)
            {
                ds.DrawText($"Mode=OverwriteSamples", 16, 44, Colors.Black, format);
                ds.DrawText($"Pressure={pressure:0.###}", 16, 72, Colors.Black, format);
                ds.DrawText($"MaxOverwrite={maxOverwrite}", 16, 100, Colors.Black, format);
            }
            else
            {
                ds.DrawText($"Mode=PressurePreset", 16, 44, Colors.Black, format);
                ds.DrawText($"Pressure={string.Join("/", MainPage.PressurePreset)}", 16, 72, Colors.Black, format);
            }

            var w = mp._lastGeneratedAttributes?.Size.Width;
            if (w != null)
            {
                ds.DrawText($"StrokeWidth={w.Value:0.##}", 16, 156, Colors.Black, format);
            }

            var c = mp._lastGeneratedAttributes?.Color;
            if (c != null)
            {
                ds.DrawText($"Color=ARGB({c.Value.A},{c.Value.R},{c.Value.G},{c.Value.B})", 16, 184, Colors.Black, format);
            }

            ds.DrawText($"Export={UIHelpers.GetExportWidth(mp)}x{UIHelpers.GetExportHeight(mp)}", 16, 212, Colors.Black, format);

            if (mp._lastWasDotGrid && mp._lastMaxOverwrite is int maxOverwrite2 && mp._lastDotGridSpacing is int spacing)
            {
                // Column labels (pressure)
                for (var col = 0; col < MainPage.DotGridPressurePreset.Length; col++)
                {
                    var x = MainPage.DefaultDotGridStartX + (col * spacing);
                    ds.DrawText($"P={MainPage.DotGridPressurePreset[col]:0.0}", x - 24, MainPage.DefaultDotGridStartY - 48, Colors.Black, format);
                }

                // Row labels (N)
                for (var row = 1; row <= maxOverwrite2; row++)
                {
                    var y = MainPage.DefaultDotGridStartY + ((row - 1) * spacing);
                    ds.DrawText($"N={row}", MainPage.DefaultDotGridStartX - 120, y - 12, Colors.Black, format);
                }
            }
            else if (mp._lastMaxOverwrite is int maxOverwrite3)
            {
                for (var i = 1; i <= maxOverwrite3; i++)
                {
                    var y = MainPage.DefaultStartY + ((i - 1) * MainPage.DefaultSpacingY);
                    ds.DrawText($"N={i}", 16, y - 12, Colors.Black, format);
                }
            }
            else
            {
                for (var i = 0; i < MainPage.PressurePreset.Length; i++)
                {
                    var y = MainPage.DefaultStartY + (i * MainPage.DefaultSpacingY);
                    ds.DrawText($"P={MainPage.PressurePreset[i]:0.0}", 16, y - 12, Colors.Black, format);
                }
            }
        }

        internal static void DrawS200LineLabels(MainPage mp, CanvasDrawingSession ds, InkDrawingAttributes attributes, float pressure, int exportSize, float x0, float x1)
        {
            var format = new CanvasTextFormat
            {
                FontSize = 18,
                WordWrapping = CanvasWordWrapping.NoWrap
            };

            ds.DrawText("Mode=Line", 16, 16, Colors.Black, format);
            ds.DrawText("S=200", 16, 40, Colors.Black, format);
            ds.DrawText($"Pressure={pressure:0.###}", 16, 64, Colors.Black, format);
            ds.DrawText($"Export={exportSize}x{exportSize}", 16, 88, Colors.Black, format);
            ds.DrawText($"X={x0:0.##}..{x1:0.##}", 16, 112, Colors.Black, format);
            ds.DrawText($"StrokeWidth={attributes.Size.Width:0.##}", 16, 136, Colors.Black, format);
            ds.DrawText($"Color=ARGB({attributes.Color.A},{attributes.Color.R},{attributes.Color.G},{attributes.Color.B})", 16, 160, Colors.Black, format);
        }

        internal static void DrawDot512Labels(CanvasDrawingSession ds, InkDrawingAttributes attributes, float pressure, int n)
        {
            var format = new CanvasTextFormat
            {
                FontSize = 18,
                WordWrapping = CanvasWordWrapping.NoWrap
            };

            ds.DrawText("Mode=Dot512", 16, 16, Colors.Black, format);
            ds.DrawText($"Pressure={pressure:0.###}", 16, 40, Colors.Black, format);
            ds.DrawText($"N={n}", 16, 64, Colors.Black, format);
            ds.DrawText($"Export=512x512", 16, 88, Colors.Black, format);

            ds.DrawText($"StrokeWidth={attributes.Size.Width:0.##}", 16, 112, Colors.Black, format);
            ds.DrawText($"Color=ARGB({attributes.Color.A},{attributes.Color.R},{attributes.Color.G},{attributes.Color.B})", 16, 136, Colors.Black, format);
        }

    }
}
