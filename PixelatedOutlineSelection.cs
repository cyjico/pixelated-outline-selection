using PaintDotNet;
using PaintDotNet.AppModel;
using PaintDotNet.Effects;
using PaintDotNet.Imaging;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using PaintDotNet.Rendering;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Windows.Forms;

[assembly: AssemblyDescription("Creates an outline around a selection of Bitmap type. Better suited for pixel-art.")]
[assembly: AssemblyCopyright("Copyright ©2024 by cyjico")]
[assembly: SupportedOSPlatform("Windows")]

namespace PDNPlugins
{
    public sealed class OutlineSelectionEffectSupportInfo : IPluginSupportInfo
    {
        public string Author => "Creates a pixelated outline around a selection.";
        public string Copyright => "Copyright ©2024 by cyjico";
        public string DisplayName => "outline";
        public Version Version => GetType().Assembly.GetName().Version ?? new Version();
        public Uri WebsiteUri => new("https://github.com/cyjico");
    }

    [PluginSupportInfo<OutlineSelectionEffectSupportInfo>(DisplayName = "Pixelated Outline Selection")]
    public sealed class PixelatedOutlineSelection : PropertyBasedBitmapEffect
    {
        private static System.Drawing.Image StaticIcon => new System.Drawing.Bitmap(
            typeof(PixelatedOutlineSelection),
            "icon.png"
        );

        /// <summary>
        /// 8-connectivity. It starts from the TOP spinning clockwise.
        /// </summary>
        private static readonly Point2Int32[] DIRS_8 = {
            new(0, -1),
            new(1, -1),
            new(1, 0),
            new(1, 1),
            new(0, 1),
            new(-1, 1),
            new(-1, 0),
            new(-1, -1),
        };

        /// <summary>
        /// Same as DIRS_8 except it priorities ORTHOGNOAL directions.
        /// </summary>
        private static readonly Point2Int32[] DIRS_8_ORTHO = {
            // ORTHOGONAL
            new(0, -1),
            new(1, 0),
            new(0, 1),
            new(-1, 0),
            new(1, -1),
            new(1, 1),
            new(-1, 1),
            new(-1, -1),
        };

        /// <summary>
        /// 4-connectivity with the same configuration as DIRS_8.
        /// </summary>
        private static readonly Point2Int32[] DIRS_4 = {
            new(1, 0),
            new(0, 1),
            new(-1, 0),
            new(0, -1),
        };

        private static readonly ColorBgra32[] COLORS = {
            ColorBgra.Red,
            ColorBgra.OrangeRed,
            ColorBgra.Orange,
            ColorBgra.Yellow,
            ColorBgra.YellowGreen,
            ColorBgra.Green,
            ColorBgra.Cyan,
            ColorBgra.Blue,
            ColorBgra.SlateBlue,
            ColorBgra.Indigo,
            ColorBgra.DarkViolet,
            ColorBgra.Violet,
        };

        public PixelatedOutlineSelection()
            : base(
                "Outline Selection, Pixelated",
                StaticIcon,
                "Selection",
                BitmapEffectOptionsFactory.Create() with
                {
                    IsConfigurable = true
                })
        {
        }

        #region UI Code

        protected override void OnCustomizeConfigUIWindowProperties(PropertyCollection props)
        {
            // Change the effect's window title
            props[ControlInfoPropertyNames.WindowTitle].Value = "Pixelated Outline Selection";
            // Add help button to effect UI
            props[ControlInfoPropertyNames.WindowHelpContentType].Value = WindowHelpContentType.CustomViaCallback;
            props[ControlInfoPropertyNames.WindowHelpContent].Value = $"{typeof(PixelatedOutlineSelection).Namespace}.help-content.rtf";
           
            base.OnCustomizeConfigUIWindowProperties(props);
        }

        #region Help Window
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern nint GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int LoadString(nint hInstance, uint uID, StringBuilder lpBuffer, int nBufferMax);

        private static string LoadLocalizedString(string libraryName, uint ident, string defaultText)
        {
            nint libraryHandle = GetModuleHandle(libraryName);
            if (libraryHandle != nint.Zero)
            {
                var sb = new StringBuilder(1024);
                if (LoadString(libraryHandle, ident, sb, 1024) > 0)
                {
                    return sb.ToString();
                }
            }

            return defaultText;
        }

        public static string DecompressString(string compressedText)
        {
            byte[] gZipBuffer = Convert.FromBase64String(compressedText);

            using (var memoryStream = new MemoryStream())
            {
                int dataLength = BitConverter.ToInt32(gZipBuffer, 0);
                memoryStream.Write(gZipBuffer, 4, gZipBuffer.Length - 4);

                var buffer = new byte[dataLength];

                memoryStream.Position = 0;
                using (var gZipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
                {
                    gZipStream.ReadExactly(buffer, 0, buffer.Length);
                }

                return Encoding.UTF8.GetString(buffer);
            }
        }

        private void OnWindowHelpButtonClicked(IWin32Window owner, string helpContent)
        {
            var assembly = Assembly.GetExecutingAssembly();
            string result = "";

            // helpContent has the resource name in it.
            using (Stream stream = assembly.GetManifestResourceStream(helpContent))
            {
                using StreamReader reader = new(stream);
                result = reader.ReadToEnd();
            }

            using (var form = new Form())
            {
                form.SuspendLayout();
                form.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
                form.AutoScaleMode = AutoScaleMode.Dpi;
                form.Text = "Outline Selection Bitmap - " + LoadLocalizedString("user32.dll", 808, "Help");
                form.AutoSize = false;
                form.ClientSize = new System.Drawing.Size(564, 392);
                form.MinimumSize = new System.Drawing.Size(330, 282);
                form.FormBorderStyle = FormBorderStyle.Sizable;
                form.ShowInTaskbar = false;
                form.MinimizeBox = false;
                form.StartPosition = FormStartPosition.CenterParent;
                form.Icon = System.Drawing.Icon.FromHandle(((System.Drawing.Bitmap)StaticIcon).GetHicon());

                var btn_HelpBoxOKButton = new Button()
                {
                    AutoSize = true,
                    Text = LoadLocalizedString("user32.dll", 800, "OK"),
                    DialogResult = DialogResult.Cancel,
                    Size = new System.Drawing.Size(84, 24),
                    Location = new System.Drawing.Point(472, 359),
                    Anchor = AnchorStyles.Bottom | AnchorStyles.Right
                };
                form.Controls.Add(btn_HelpBoxOKButton);

                var rtb_HelpBox = new RichTextBox()
                {
                    Size = new System.Drawing.Size(564, 350),
                    Location = new System.Drawing.Point(0, 0),
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right,
                    DetectUrls = true,
                    WordWrap = true,
                    ScrollBars = RichTextBoxScrollBars.ForcedVertical,
                    BorderStyle = BorderStyle.None
                };
                rtb_HelpBox.Font = new System.Drawing.Font(rtb_HelpBox.SelectionFont.Name, 10f);
                rtb_HelpBox.LinkClicked += new LinkClickedEventHandler((sender, e) =>
                {
                    Services?.GetService<IShellService>()?.LaunchUrl(null, e?.LinkText ?? "");
                    btn_HelpBoxOKButton.Focus();
                });
                rtb_HelpBox.ReadOnly = false;
                rtb_HelpBox.Rtf = result;
                rtb_HelpBox.ReadOnly = true;
                form.Controls.Add(rtb_HelpBox);

                form.ResumeLayout();
                form.ShowDialog();
            }
        }
        #endregion

        private ColorBgra32 Otl_Color = ColorBgra.FromBgr(0, 0, 255);
        private bool Otl_IsDsh = true;
        private int Otl_DshOffset = 1;
        private int Otl_DshSegLen = 2;
        private int Otl_DshSegGap = 1;

        public enum PropertyNames
        {
            Otl_Color,
            Otl_IsDsh,
            Otl_DshOffset,
            Otl_DshSegLen,
            Otl_DshSegGap
        }

        protected override PropertyCollection OnCreatePropertyCollection()
        {
            var props = new List<Property>
        {
            new Int32Property(PropertyNames.Otl_Color, ColorBgra.ToOpaqueInt32(ColorBgra.Red), 0, 0xffffff),
            new BooleanProperty(PropertyNames.Otl_IsDsh, true),
            new Int32Property(PropertyNames.Otl_DshOffset, 1, 0, 16),
            new Int32Property(PropertyNames.Otl_DshSegLen, 2, 1, 4),
            new Int32Property(PropertyNames.Otl_DshSegGap, 1, 1, 4)
        };

            var propRules = new List<PropertyCollectionRule>();

            return new PropertyCollection(props, propRules);
        }

        protected override ControlInfo OnCreateConfigUI(PropertyCollection props)
        {
            ControlInfo configUI = CreateDefaultConfigUI(props);

            configUI.SetPropertyControlValue(PropertyNames.Otl_Color, ControlInfoPropertyNames.DisplayName, "Color");
            configUI.SetPropertyControlType(PropertyNames.Otl_Color, PropertyControlType.ColorWheel);
            configUI.SetPropertyControlValue(PropertyNames.Otl_Color, ControlInfoPropertyNames.ShowHeaderLine, false);

            configUI.SetPropertyControlValue(PropertyNames.Otl_IsDsh, ControlInfoPropertyNames.DisplayName, string.Empty);
            configUI.SetPropertyControlValue(PropertyNames.Otl_IsDsh, ControlInfoPropertyNames.Description, "Is Dashed");
            configUI.SetPropertyControlValue(PropertyNames.Otl_IsDsh, ControlInfoPropertyNames.ShowHeaderLine, true);

            configUI.SetPropertyControlValue(PropertyNames.Otl_DshOffset, ControlInfoPropertyNames.DisplayName, "Dashed Offset");
            configUI.SetPropertyControlValue(PropertyNames.Otl_DshOffset, ControlInfoPropertyNames.ShowHeaderLine, false);

            configUI.SetPropertyControlValue(PropertyNames.Otl_DshSegLen, ControlInfoPropertyNames.DisplayName, "Dashed Segment Length");
            configUI.SetPropertyControlValue(PropertyNames.Otl_DshSegLen, ControlInfoPropertyNames.ShowHeaderLine, false);

            configUI.SetPropertyControlValue(PropertyNames.Otl_DshSegGap, ControlInfoPropertyNames.DisplayName, "Dashed Segment Gap");
            configUI.SetPropertyControlValue(PropertyNames.Otl_DshSegGap, ControlInfoPropertyNames.ShowHeaderLine, false);

            return configUI;
        }

        protected override void OnInitializeRenderInfo(IBitmapEffectRenderInfo renderInfo)
        {
            renderInfo.Flags = BitmapEffectRenderingFlags.ForceAliasedSelectionQuality | BitmapEffectRenderingFlags.UninitializedOutputBuffer;

            base.OnInitializeRenderInfo(renderInfo);
        }

        protected override void OnSetToken(PropertyBasedEffectConfigToken? newToken)
        {
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            Otl_Color = ColorBgra.FromOpaqueInt32(newToken.GetProperty<Int32Property>(PropertyNames.Otl_Color).Value);
            Otl_IsDsh = newToken.GetProperty<BooleanProperty>(PropertyNames.Otl_IsDsh).Value;
            Otl_DshOffset = newToken.GetProperty<Int32Property>(PropertyNames.Otl_DshOffset).Value;
            Otl_DshSegLen = newToken.GetProperty<Int32Property>(PropertyNames.Otl_DshSegLen).Value;
            Otl_DshSegGap = newToken.GetProperty<Int32Property>(PropertyNames.Otl_DshSegGap).Value;
#pragma warning restore CS8602 // Dereference of a possibly null reference.

            base.OnSetToken(newToken);
        }

        #endregion

        // Why class instead of struct? I need it for "referencing".
        private class Pixel
        {
            public int X;
            public int Y;
            public bool IsFilled = true;

            public Pixel? PreviousPixel = null;

            public Pixel(int x, int y)
            {
                X = x;
                Y = y;
            }

            public static implicit operator Point2Int32(Pixel pixel) => new(pixel.X, pixel.Y);
            public static explicit operator Pixel(Point2Int32 point) => new(point.X, point.Y);

            public override string ToString() => $"{X}, {Y}";

            public override bool Equals(object? obj)
            {
                if (obj is not Pixel pixel)
                {
                    return false;
                }

                return X.Equals(pixel.X) && Y.Equals(pixel.Y);
            }

            public override int GetHashCode() => HashCode.Combine(X, Y);
        }

        protected override void OnRender(IBitmapEffectOutput output)
        {
            using IEffectInputBitmap<ColorBgra32> sourceBitmap = Environment.GetSourceBitmapBgra32();
            using IBitmapLock<ColorBgra32> sourceLock = sourceBitmap.Lock(new RectInt32(0, 0, sourceBitmap.Size));
            _ = sourceLock.AsRegionPtr();

            RectInt32 outputBounds = output.Bounds;
            using IBitmapLock<ColorBgra32> outputLock = output.LockBgra32();
            RegionPtr<ColorBgra32> outputSubRegion = outputLock.AsRegionPtr();
            RegionPtrOffsetView<ColorBgra32> outputRegion = outputSubRegion.OffsetView(-outputBounds.Location);

            int lastRoiIndex = 0;
            var visited = new HashSet<Pixel>();

            while (TryFindStartingPixel(ref lastRoiIndex, ref visited, out Pixel start))
            {
                if (!TryRenderBoundary(outputBounds, ref outputRegion, ref visited, start)) return;
            }
        }

        #region HelperCode
        private bool IsInsideSelection(int x, int y)
        {
            // Please see for ROIs: https://boltbait.com/pdn/CodeLab/help/overview.php
            IReadOnlyList<RectInt32> rois = Environment.Selection.RenderScans;

            foreach (RectInt32 roi in rois)
            {
                if (roi.Contains(x, y)) return true;
            }

            return false;
        }

        private bool IsInsideSelection(Point2Int32 point) => IsInsideSelection(point.X, point.Y);

        private bool IsEdgePixel(int x, int y)
        {
            if (!IsInsideSelection(x, y)) return false;

            for (int i = 0; i < 4; i++)
            {
                if (!IsInsideSelection(x + DIRS_4[i].X, y + DIRS_4[i].Y))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryFindStartingPixel(ref int lastRoiIndex, ref HashSet<Pixel> visited, out Pixel start)
        {
            start = new Pixel(0, 0);

            // Logic variables.
            IReadOnlyList<RectInt32> rois = Environment.Selection.RenderScans;

            for (; lastRoiIndex < rois.Count; lastRoiIndex++)
            {
                for (int y = rois[lastRoiIndex].Top; y < rois[lastRoiIndex].Bottom; y++)
                {
                    for (int x = rois[lastRoiIndex].Left; x < rois[lastRoiIndex].Right; x++)
                    {
                        if (IsCancelRequested) return false;

                        var pixel = new Pixel(x, y);

                        if (!visited.Contains(pixel))
                        {
                            if (IsEdgePixel(x, y))
                            {
                                start = pixel;

                                return true;
                            }

                            visited.Add(pixel);
                        }
                    }
                }
            }

            return false;
        }

        private bool TryRenderPixel(RectInt32 outputBounds, ref RegionPtrOffsetView<ColorBgra32> outputRegion, Pixel pixel)
        {
            if (!outputBounds.Contains(pixel)) return false;

            outputRegion[pixel] = pixel.IsFilled ? Otl_Color : ColorBgra.TransparentBlack;

            if (pixel.PreviousPixel == null || pixel.PreviousPixel.IsFilled != pixel.IsFilled || Otl_DshSegGap + Otl_DshSegLen <= 2) return true;

            int lengthSquared;
            {
                int dir_x = pixel.PreviousPixel.X - pixel.X;
                int dir_y = pixel.PreviousPixel.Y - pixel.Y;
                lengthSquared = dir_x * dir_x + dir_y * dir_y;
            }

            if (lengthSquared > 1f)
            {
                var pointX = new Point2Int32(pixel.PreviousPixel.X, pixel.Y);
                var pointY = new Point2Int32(pixel.X, pixel.PreviousPixel.Y);

                if (outputBounds.Contains(pointX) && IsInsideSelection(pointX))
                {
                    outputRegion[pointX] = pixel.PreviousPixel.IsFilled ? Otl_Color : ColorBgra.TransparentBlack;
                }
                else if (outputBounds.Contains(pointY) && IsInsideSelection(pointY))
                {
                    outputRegion[pointY] = pixel.PreviousPixel.IsFilled ? Otl_Color : ColorBgra.TransparentBlack;
                }
            }

            return true;
        }

        // Non-recursive depth-first search.
        private bool TryRenderBoundary(RectInt32 outputBounds, ref RegionPtrOffsetView<ColorBgra32> outputRegion, ref HashSet<Pixel> visited, Pixel start)
        {
            var stack = new Stack<Pixel>();
            stack.Push(start);

            int i = 0;
            while (stack.Count > 0)
            {
                if (IsCancelRequested) return false;

                Pixel cur = stack.Pop();

                if (visited.Contains(cur))
                {
                    if (cur.PreviousPixel?.PreviousPixel == null)
                    {
                        cur.PreviousPixel!.PreviousPixel = cur;

                        TryRenderPixel(outputBounds, ref outputRegion, cur.PreviousPixel);
                    }

                    continue;
                }

                visited.Add(cur);

                cur.IsFilled = !Otl_IsDsh || (i + Otl_DshOffset) % (Otl_DshSegLen + Otl_DshSegGap) >= Otl_DshSegGap;
                TryRenderPixel(outputBounds, ref outputRegion, cur);

                // Find the next neighboring EDGE pixels to render.
                for (int j = 7; j >= 0; j--)
                {
                    var nbor = new Pixel(cur.X + DIRS_8_ORTHO[j].X, cur.Y + DIRS_8_ORTHO[j].Y);

                    if (!visited.Contains(nbor) && IsEdgePixel(nbor.X, nbor.Y))
                    {
                        nbor.PreviousPixel = cur;

                        stack.Push(nbor);
                    }
                }

                i++;
            }

            return true;
        }

        #endregion
    }
}