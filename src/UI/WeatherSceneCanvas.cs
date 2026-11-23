using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PaperWeatherWidget.UI
{
    public enum WeatherSceneKind
    {
        ClearDay,
        PartlyCloudy,
        Cloudy,
        Fog,
        Rain,
        HeavyRain,
        Storm,
        ClearNight,
        Snow
    }

    /// <summary>
    /// Ultra-low-CPU (<1%) Apple Weather + iOS Liquid Glass rendering engine.
    /// Architecture:
    /// - BackgroundLayer: Grid hosting (a) pre-baked static backdrop elements and (b) a single lightweight
    ///   OnRender dynamic surface using 1x pre-baked sprites with zero PushOpacity loops.
    /// - ForegroundGlassLayer: Grid hosting (a) a ONCE-baked static frosted condensation + clinging-drop Image
    ///   (ZERO per-frame cost!) and (b) a tiny dynamic OnRender surface for only the 4 sliding rivulets + rim splashes.
    /// </summary>
    public sealed class WeatherSceneController
    {
        private sealed class RenderSurface : FrameworkElement
        {
            private readonly Action<DrawingContext> renderCallback;

            public RenderSurface(Action<DrawingContext> onRender)
            {
                renderCallback = onRender;
                IsHitTestVisible = false;
                ClipToBounds = true;
                RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
            }

            protected override void OnRender(DrawingContext dc)
            {
                if (renderCallback != null)
                {
                    renderCallback(dc);
                }
            }
        }

        private struct CloudParticle
        {
            public double X, Y, W, H, Vx;
            public bool Bright;
        }

        private struct RainStreak
        {
            public double X, Y, Vx, Vy, Len, TargetRimY;
            public bool HitsRim;
        }

        private struct RimSplash
        {
            public double X, Y, Age, Life, Scale;
            public bool Active;
        }

        private struct SlidingRivulet
        {
            public double X, BaseX, Y, W, H, Vy, Pause, Phase;
            public double TrailTopY;
            public double TrailAlpha;
        }

        private struct StarParticle
        {
            public double X, Y, R, Phase, Speed;
        }

        private struct SnowParticle
        {
            public double X, BaseX, Y, R, Vy, Phase, Amp, Speed;
        }

        // Pre-baked 1x frozen optical sprites (~90 KB total across the entire app!)
        private static BitmapSource spriteSunRays;
        private static BitmapSource spriteSunCore;
        private static BitmapSource spriteCloudBright;
        private static BitmapSource spriteCloudMoody;
        private static BitmapSource spriteMoonGlow;
        private static BitmapSource spriteDropTeardrop;
        private static BitmapSource spriteDropCling;
        private static BitmapSource spriteDropMicro;

        // Pre-allocated frozen pens & brushes (zero per-frame GC, zero PushOpacity layers!)
        private static readonly Pen RainPenLight = MakeFrozenPen("#48D8EEFF", 1.0);
        private static readonly Pen RainPenHeavy = MakeFrozenPen("#64E0F2FE", 1.25);
        private static readonly Pen WetTrailPen = MakeFrozenPen("#24E0F2FE", 1.15);
        private static readonly Pen ShootStarPen = MakeFrozenPen("#CCF8FAFC", 1.3);
        private static readonly Brush[] SplashBrushes = new Brush[]
        {
            ThemePalette.FreezeBrush("#88E6F4FF"),
            ThemePalette.FreezeBrush("#60E6F4FF"),
            ThemePalette.FreezeBrush("#38E6F4FF"),
            ThemePalette.FreezeBrush("#1AE6F4FF")
        };
        private static readonly Brush[] StarBrushes = new Brush[]
        {
            ThemePalette.FreezeBrush("#48F8FAFC"),
            ThemePalette.FreezeBrush("#82F8FAFC"),
            ThemePalette.FreezeBrush("#BDF8FAFC"),
            ThemePalette.FreezeBrush("#F2F8FAFC")
        };
        private static readonly Brush SnowBrush = MakeSoftRadialBrush("#CCFFFFFF", "#00E0F2FE");
        private static readonly Brush LensFlareBrush = MakeSoftRadialBrush("#26FEF9C3", "#00FFFFFF");
        private static readonly Brush LightningBrush = MakeLightningBrush();

        private readonly Grid bgHost;
        private readonly Image bgStaticCloudImage;
        private readonly RenderSurface bgDynamicSurface;
        private readonly Grid fgHost;
        private readonly Image fgStaticCondensationImage;
        private readonly RenderSurface fgDynamicSurface;
        private readonly DispatcherTimer animTimer;

        private WeatherSceneKind currentKind = WeatherSceneKind.PartlyCloudy;
        private bool sceneBuilt;
        private double sceneWidth = 348;
        private double sceneHeight = 330;
        private double primaryCardRimY = 202;
        private double elapsedSec;
        private bool animationsEnabled;

        private readonly List<CloudParticle> clouds = new List<CloudParticle>();
        private readonly List<RainStreak> rainStreaks = new List<RainStreak>();
        private readonly List<RimSplash> rimSplashes = new List<RimSplash>();
        private readonly List<SlidingRivulet> rivulets = new List<SlidingRivulet>();
        private readonly List<StarParticle> stars = new List<StarParticle>();
        private readonly List<SnowParticle> snowFlakes = new List<SnowParticle>();

        public FrameworkElement BackgroundLayer { get { return bgHost; } }
        public FrameworkElement ForegroundGlassLayer { get { return fgHost; } }
        public WeatherSceneKind CurrentKind { get { return currentKind; } }

        public WeatherSceneController()
        {
            EnsureOpticalSpritesBaked();

            bgHost = new Grid { IsHitTestVisible = false, ClipToBounds = true };
            bgStaticCloudImage = new Image { IsHitTestVisible = false, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(bgStaticCloudImage, BitmapScalingMode.Linear);
            bgDynamicSurface = new RenderSurface(RenderBackground);
            bgHost.Children.Add(bgStaticCloudImage);
            bgHost.Children.Add(bgDynamicSurface);

            fgHost = new Grid { IsHitTestVisible = false, ClipToBounds = true };
            fgStaticCondensationImage = new Image
            {
                IsHitTestVisible = false,
                Stretch = Stretch.Fill
            };
            RenderOptions.SetBitmapScalingMode(fgStaticCondensationImage, BitmapScalingMode.Linear);
            fgDynamicSurface = new RenderSurface(RenderForegroundDynamic);
            fgHost.Children.Add(fgStaticCondensationImage);
            fgHost.Children.Add(fgDynamicSurface);

            animTimer = new DispatcherTimer(DispatcherPriority.Render);
            animTimer.Interval = TimeSpan.FromMilliseconds(62);
            animTimer.Tick += OnFrameTick;
        }

        public static WeatherSceneKind ResolveSceneKind(int? wmoCode, int? isDay)
        {
            var code = wmoCode ?? 2;
            var night = isDay.HasValue && isDay.Value == 0;
            if (code >= 95) { return WeatherSceneKind.Storm; }
            if ((code >= 71 && code <= 77) || code == 85 || code == 86) { return WeatherSceneKind.Snow; }
            if (code == 65 || code == 82 || code == 55) { return WeatherSceneKind.HeavyRain; }
            if ((code >= 51 && code <= 67) || (code >= 80 && code <= 82)) { return WeatherSceneKind.Rain; }
            if (code == 45 || code == 48) { return WeatherSceneKind.Fog; }
            if (night) { return WeatherSceneKind.ClearNight; }
            if (code <= 1) { return WeatherSceneKind.ClearDay; }
            if (code == 2) { return WeatherSceneKind.PartlyCloudy; }
            return WeatherSceneKind.Cloudy;
        }

        public void SetViewportSize(double width, double height)
        {
            sceneWidth = Math.Max(200, width);
            sceneHeight = Math.Max(200, height);
            bgHost.Width = sceneWidth;
            bgHost.Height = sceneHeight;
            bgStaticCloudImage.Width = sceneWidth;
            bgStaticCloudImage.Height = sceneHeight;
            bgDynamicSurface.Width = sceneWidth;
            bgDynamicSurface.Height = sceneHeight;
            fgHost.Width = sceneWidth;
            fgHost.Height = sceneHeight;
            fgStaticCondensationImage.Width = sceneWidth;
            fgStaticCondensationImage.Height = sceneHeight;
            fgDynamicSurface.Width = sceneWidth;
            fgDynamicSurface.Height = sceneHeight;
        }

        public void SetScene(WeatherSceneKind kind, bool forceRebuild)
        {
            if (sceneBuilt && currentKind == kind && !forceRebuild)
            {
                return;
            }
            currentKind = kind;
            sceneBuilt = true;
            RebuildParticles();
            UpdateTimerCadence();
            bgDynamicSurface.InvalidateVisual();
            fgDynamicSurface.InvalidateVisual();
        }

        public void SetRunning(bool running)
        {
            animationsEnabled = running;
            UpdateTimerCadence();
            if (running)
            {
                if (!animTimer.IsEnabled) { animTimer.Start(); }
            }
            else
            {
                if (animTimer.IsEnabled) { animTimer.Stop(); }
            }
        }

        public void AdvanceDeterministic(double seconds)
        {
            elapsedSec = seconds;
            StepPhysics(0.062, true);
            bgDynamicSurface.InvalidateVisual();
            fgDynamicSurface.InvalidateVisual();
        }

        private void UpdateTimerCadence()
        {
            var isFastScene = currentKind == WeatherSceneKind.Rain
                || currentKind == WeatherSceneKind.HeavyRain
                || currentKind == WeatherSceneKind.Storm
                || currentKind == WeatherSceneKind.Snow;
            // 12fps for smooth rain/snow, 8fps for serene sun/clouds/stars -> <1% CPU in SoftwareOnly WPF!
            animTimer.Interval = TimeSpan.FromMilliseconds(isFastScene ? 83 : 120);
        }

        private void OnFrameTick(object sender, EventArgs e)
        {
            if (!animationsEnabled) { return; }
            var dt = animTimer.Interval.TotalSeconds;
            elapsedSec += dt;
            StepPhysics(dt, false);
            bgDynamicSurface.InvalidateVisual();
            if (HasForegroundGlassFx())
            {
                fgDynamicSurface.InvalidateVisual();
            }
        }

        private bool HasForegroundGlassFx()
        {
            return currentKind == WeatherSceneKind.Rain
                || currentKind == WeatherSceneKind.HeavyRain
                || currentKind == WeatherSceneKind.Storm;
        }

        // ==================== PARTICLE INITIALIZATION ====================
        private void RebuildParticles()
        {
            clouds.Clear();
            rainStreaks.Clear();
            rimSplashes.Clear();
            rivulets.Clear();
            stars.Clear();
            snowFlakes.Clear();
            bgStaticCloudImage.Source = null;
            fgStaticCondensationImage.Source = null;

            var rng = new Random(4117);

            switch (currentKind)
            {
                case WeatherSceneKind.ClearDay:
                    break;

                case WeatherSceneKind.PartlyCloudy:
                    InitClouds(3, false);
                    break;

                case WeatherSceneKind.Cloudy:
                case WeatherSceneKind.Fog:
                    InitClouds(4, true);
                    break;

                case WeatherSceneKind.Rain:
                    InitRainAndGlassDrops(14, false, rng);
                    break;

                case WeatherSceneKind.HeavyRain:
                case WeatherSceneKind.Storm:
                    InitRainAndGlassDrops(20, true, rng);
                    break;

                case WeatherSceneKind.ClearNight:
                    // Keep stars cleanly in the upper open sky so text and middle controls stay spotless!
                    for (var i = 0; i < 22; i++)
                    {
                        double sx, sy;
                        if (i % 3 == 0)
                        {
                            sx = 178 + rng.NextDouble() * (sceneWidth - 196);
                            sy = 14 + rng.NextDouble() * 130;
                        }
                        else
                        {
                            sx = 18 + rng.NextDouble() * (sceneWidth - 36);
                            sy = 10 + rng.NextDouble() * 34;
                        }
                        stars.Add(new StarParticle
                        {
                            X = sx,
                            Y = sy,
                            R = (i % 5 == 0) ? 1.45 : (0.65 + rng.NextDouble() * 0.55),
                            Phase = rng.NextDouble() * 6.28,
                            Speed = 1.1 + rng.NextDouble() * 1.8
                        });
                    }
                    break;

                case WeatherSceneKind.Snow:
                    InitClouds(2, true);
                    for (var i = 0; i < 24; i++)
                    {
                        var x = rng.NextDouble() * sceneWidth;
                        snowFlakes.Add(new SnowParticle
                        {
                            X = x,
                            BaseX = x,
                            Y = rng.NextDouble() * sceneHeight,
                            R = 1.6 + rng.NextDouble() * 2.2,
                            Vy = 18 + rng.NextDouble() * 22,
                            Phase = rng.NextDouble() * 6.28,
                            Amp = 7 + rng.NextDouble() * 9,
                            Speed = 1.0 + rng.NextDouble() * 1.1
                        });
                    }
                    break;
            }
        }

        private void InitClouds(int count, bool moody)
        {
            for (var i = 0; i < count; i++)
            {
                clouds.Add(new CloudParticle
                {
                    X = -35 + i * 105,
                    Y = 8 + (i % 3) * 32,
                    W = 165 + i * 32,
                    H = 66 + i * 10,
                    Vx = 4.2 + i * 2.6,
                    Bright = !moody
                });
            }
        }

        private void InitRainAndGlassDrops(int streakCount, bool heavy, Random rng)
        {
            // 1. Falling background rain streaks
            for (var i = 0; i < streakCount; i++)
            {
                var hitsRim = (i % 3 == 0);
                var vy = (heavy ? 290 : 220) + rng.NextDouble() * 110;
                rainStreaks.Add(new RainStreak
                {
                    X = rng.NextDouble() * (sceneWidth + 45),
                    Y = rng.NextDouble() * (hitsRim ? primaryCardRimY : sceneHeight),
                    Vx = -vy * 0.14,
                    Vy = vy,
                    Len = (heavy ? 22 : 16) + rng.Next(0, 12),
                    TargetRimY = primaryCardRimY,
                    HitsRim = hitsRim
                });
            }

            // 2. Pool of 8 Apple-style card-rim splash crowns
            for (var i = 0; i < 8; i++)
            {
                rimSplashes.Add(new RimSplash
                {
                    X = 34 + (i / 8.0) * (sceneWidth - 68),
                    Y = primaryCardRimY,
                    Age = (i * 0.08) % 0.36,
                    Life = 0.34,
                    Scale = 0.8 + (i % 3) * 0.25,
                    Active = true
                });
            }

            // Bake soft storm cloud canopy ONCE behind the text, and frosted condensation ONCE on top!
            bgStaticCloudImage.Source = BakeStaticStormClouds((int)sceneWidth, (int)sceneHeight);
            fgStaticCondensationImage.Source = BakeStaticGlassCondensation((int)sceneWidth, (int)sceneHeight, heavy, rng);

            // 4. Only 4-5 active sliding gravity rivulets need dynamic per-frame animation!
            var rivuletCount = heavy ? 5 : 4;
            var rivXSlots = new[] { sceneWidth - 36, sceneWidth - 74, 15.0, sceneWidth - 19, sceneWidth - 100 };
            for (var i = 0; i < rivuletCount; i++)
            {
                var bx = rivXSlots[i % rivXSlots.Length] + (rng.NextDouble() - 0.5) * 8;
                var startY = 48 + rng.NextDouble() * (sceneHeight * 0.52);
                var w = 5.4 + rng.NextDouble() * 2.4;
                rivulets.Add(new SlidingRivulet
                {
                    X = bx,
                    BaseX = bx,
                    Y = startY,
                    W = w,
                    H = w * 1.52,
                    Vy = 24 + rng.NextDouble() * 24,
                    Pause = (i % 2 == 0) ? 0.0 : (0.8 + rng.NextDouble() * 1.4),
                    Phase = rng.NextDouble() * 6.28,
                    TrailTopY = Math.Max(34, startY - 38 - rng.NextDouble() * 30),
                    TrailAlpha = 0.7
                });
            }
        }

        private static BitmapSource BakeStaticStormClouds(int w, int h)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                if (spriteCloudMoody != null)
                {
                    dc.DrawImage(spriteCloudMoody, new Rect(-24, 6, 195, 76));
                    dc.DrawImage(spriteCloudMoody, new Rect(95, 24, 210, 82));
                    dc.DrawImage(spriteCloudMoody, new Rect(180, 4, 185, 74));
                }
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        private BitmapSource BakeStaticGlassCondensation(int w, int h, bool heavy, Random rng)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Subtle glistening wet sheen along the top glass rim of the primary metrics card
                dc.DrawRectangle(MakeHorizontalWetRimBrush(), null, new Rect(22, primaryCardRimY - 1, w - 44, 2.2));

                // Organic micro-condensation mist specks
                var microCount = heavy ? 44 : 32;
                for (var i = 0; i < microCount; i++)
                {
                    double x, y;
                    var band = i % 5;
                    if (band == 0)
                    {
                        x = w - 115 + rng.NextDouble() * 105;
                        y = 38 + rng.NextDouble() * 135;
                    }
                    else if (band == 1)
                    {
                        x = 5 + rng.NextDouble() * 26;
                        y = 30 + rng.NextDouble() * (h - 55);
                    }
                    else if (band == 2)
                    {
                        x = w - 28 + rng.NextDouble() * 22;
                        y = 36 + rng.NextDouble() * (h - 60);
                    }
                    else if (band == 3)
                    {
                        x = 24 + rng.NextDouble() * (w - 48);
                        y = primaryCardRimY - 3 + rng.NextDouble() * 8;
                    }
                    else
                    {
                        x = 18 + rng.NextDouble() * (w - 36);
                        y = h - 36 + rng.NextDouble() * 28;
                    }

                    var sz = 2.0 + rng.NextDouble() * 2.8;
                    dc.PushOpacity(0.45 + rng.NextDouble() * 0.35);
                    dc.DrawImage(spriteDropMicro, new Rect(x, y, sz, sz * (1.0 + rng.NextDouble() * 0.22)));
                    dc.Pop();
                }

                // Medium organic clinging teardrops & surface-tension beads
                var clingCount = heavy ? 11 : 8;
                for (var i = 0; i < clingCount; i++)
                {
                    double x, y;
                    if (i % 3 == 0)
                    {
                        x = w - 96 + rng.NextDouble() * 78;
                        y = 54 + rng.NextDouble() * 110;
                    }
                    else if (i % 3 == 1)
                    {
                        x = 8 + rng.NextDouble() * 20;
                        y = 72 + rng.NextDouble() * (h - 130);
                    }
                    else
                    {
                        x = w - 26 + rng.NextDouble() * 16;
                        y = 95 + rng.NextDouble() * (h - 140);
                    }

                    var dw = 4.8 + rng.NextDouble() * 3.8;
                    var isTeardrop = (i % 2 == 0);
                    dc.PushOpacity(0.62 + rng.NextDouble() * 0.26);
                    dc.DrawImage(isTeardrop ? spriteDropTeardrop : spriteDropCling, new Rect(x, y, dw, dw * (isTeardrop ? 1.42 : 1.08)));
                    dc.Pop();
                }
            }

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        // ==================== PHYSICS STEP ====================
        private void StepPhysics(double dt, bool deterministicPreview)
        {
            for (var i = 0; i < clouds.Count; i++)
            {
                var c = clouds[i];
                c.X += c.Vx * dt;
                if (c.X > sceneWidth + 20) { c.X = -c.W - 14; }
                clouds[i] = c;
            }

            for (var i = 0; i < rainStreaks.Count; i++)
            {
                var r = rainStreaks[i];
                r.X += r.Vx * dt;
                r.Y += r.Vy * dt;

                var maxY = r.HitsRim ? r.TargetRimY : (sceneHeight + 12);
                if (r.Y >= maxY || r.X < -24)
                {
                    if (r.HitsRim && r.X >= 22 && r.X <= sceneWidth - 22 && rimSplashes.Count > 0)
                    {
                        var spIdx = i % rimSplashes.Count;
                        var sp = rimSplashes[spIdx];
                        sp.X = r.X;
                        sp.Y = r.TargetRimY;
                        sp.Age = 0;
                        sp.Active = true;
                        rimSplashes[spIdx] = sp;
                    }
                    r.Y = -r.Len - 4;
                    r.X = ((i * 59.3 + elapsedSec * 41.0) % (sceneWidth + 55));
                }
                rainStreaks[i] = r;
            }

            for (var i = 0; i < rimSplashes.Count; i++)
            {
                var sp = rimSplashes[i];
                if (deterministicPreview)
                {
                    sp.Active = (i % 2 == 0);
                    sp.Age = 0.08 + (i % 3) * 0.06;
                    rimSplashes[i] = sp;
                }
                else if (sp.Active)
                {
                    sp.Age += dt;
                    if (sp.Age >= sp.Life) { sp.Active = false; }
                    rimSplashes[i] = sp;
                }
            }

            for (var i = 0; i < rivulets.Count; i++)
            {
                var rv = rivulets[i];
                if (rv.Pause > 0 && !deterministicPreview)
                {
                    rv.Pause -= dt;
                    rv.TrailAlpha = Math.Max(0.15, rv.TrailAlpha - dt * 0.25);
                }
                else
                {
                    rv.Y += rv.Vy * dt;
                    rv.X = rv.BaseX + Math.Sin(rv.Y * 0.055 + rv.Phase) * 2.2;
                    rv.TrailTopY = Math.Max(rv.TrailTopY, rv.Y - 62);
                    rv.TrailAlpha = Math.Min(0.85, rv.TrailAlpha + dt * 0.6);

                    if (rv.Y > sceneHeight + 10)
                    {
                        rv.Y = 38 + (i * 19.0 % 46.0);
                        rv.TrailTopY = rv.Y;
                        rv.TrailAlpha = 0.0;
                        rv.Pause = 1.1 + (i % 3) * 0.8;
                    }
                }
                rivulets[i] = rv;
            }

            for (var i = 0; i < snowFlakes.Count; i++)
            {
                var f = snowFlakes[i];
                f.Y += f.Vy * dt;
                f.X = f.BaseX + Math.Sin(elapsedSec * f.Speed + f.Phase) * f.Amp;
                if (f.Y > sceneHeight + 6) { f.Y = -6; }
                snowFlakes[i] = f;
            }
        }

        // ==================== FAST ZERO-PUSHOPACITY RENDERING ====================
        private void RenderBackground(DrawingContext dc)
        {
            if (currentKind == WeatherSceneKind.ClearDay || currentKind == WeatherSceneKind.PartlyCloudy)
            {
                var isClear = currentKind == WeatherSceneKind.ClearDay;
                var cx = sceneWidth - (isClear ? 50.0 : 54.0);
                var cy = isClear ? 72.0 : 68.0;
                var s = isClear ? 1.0 : 0.78;

                if (isClear && spriteSunRays != null)
                {
                    var raySize = 250.0 * s;
                    dc.PushTransform(new RotateTransform((elapsedSec * 4.5) % 360.0, cx, cy));
                    dc.DrawImage(spriteSunRays, new Rect(cx - raySize / 2.0, cy - raySize / 2.0, raySize, raySize));
                    dc.Pop();
                }

                if (spriteSunCore != null)
                {
                    var pulse = 1.0 + Math.Sin(elapsedSec * 1.4) * 0.04;
                    var coreSize = 152.0 * s * pulse;
                    dc.DrawImage(spriteSunCore, new Rect(cx - coreSize / 2.0, cy - coreSize / 2.0, coreSize, coreSize));
                }

                if (isClear)
                {
                    dc.DrawEllipse(LensFlareBrush, null, new Point(198 + Math.Sin(elapsedSec * 0.5) * 5, 112), 20, 20);
                    dc.DrawEllipse(LensFlareBrush, null, new Point(134 + Math.Sin(elapsedSec * 0.45 + 1.4) * 6, 152), 30, 30);
                }
            }

            if (currentKind == WeatherSceneKind.ClearNight)
            {
                var cx = sceneWidth - 54.0;
                var cy = 72.0;
                if (spriteMoonGlow != null)
                {
                    dc.DrawImage(spriteMoonGlow, new Rect(cx - 84, cy - 84, 168, 168));
                }

                for (var i = 0; i < stars.Count; i++)
                {
                    var st = stars[i];
                    var v = 0.5 + 0.5 * Math.Sin(elapsedSec * st.Speed + st.Phase);
                    var bIdx = Math.Min(3, Math.Max(0, (int)(v * 3.99)));
                    dc.DrawEllipse(StarBrushes[bIdx], null, new Point(st.X, st.Y), st.R, st.R);
                }

                var cycle = elapsedSec % 7.5;
                if (cycle < 0.75)
                {
                    var t = cycle / 0.75;
                    var sx = 32 + t * 175;
                    var sy = 20 + t * 68;
                    dc.DrawLine(ShootStarPen, new Point(sx, sy), new Point(sx + 32, sy + 12));
                }
            }

            // Drifting pre-baked soft clouds (baked opacity -> ZERO PushOpacity layers!)
            for (var i = 0; i < clouds.Count; i++)
            {
                var c = clouds[i];
                var spr = c.Bright ? spriteCloudBright : spriteCloudMoody;
                if (spr != null)
                {
                    dc.DrawImage(spr, new Rect(c.X, c.Y, c.W, c.H));
                }
            }

            if (currentKind == WeatherSceneKind.Storm)
            {
                var cycle = elapsedSec % 6.8;
                var flash = (cycle > 5.8 && cycle < 5.98) || (Math.Abs(elapsedSec - 2.2) < 0.08);
                if (flash)
                {
                    dc.DrawRectangle(LightningBrush, null, new Rect(0, 0, sceneWidth, sceneHeight));
                }
            }

            if (rainStreaks.Count > 0)
            {
                var pen = (currentKind == WeatherSceneKind.Rain) ? RainPenLight : RainPenHeavy;
                for (var i = 0; i < rainStreaks.Count; i++)
                {
                    var r = rainStreaks[i];
                    dc.DrawLine(pen, new Point(r.X, r.Y), new Point(r.X - r.Len * 0.14, r.Y + r.Len));
                }
            }

            for (var i = 0; i < snowFlakes.Count; i++)
            {
                var f = snowFlakes[i];
                dc.DrawEllipse(SnowBrush, null, new Point(f.X, f.Y), f.R, f.R);
            }
        }

        private void RenderForegroundDynamic(DrawingContext dc)
        {
            if (!HasForegroundGlassFx()) { return; }

            // 1. Apple Weather card-rim micro-splashes (zero PushOpacity -> bucketed frozen brushes!)
            for (var i = 0; i < rimSplashes.Count; i++)
            {
                var sp = rimSplashes[i];
                if (!sp.Active) { continue; }
                var t = Math.Max(0.0, Math.Min(0.99, sp.Age / sp.Life));
                var brush = SplashBrushes[(int)(t * 4.0)];
                var hopY = 8.5 * sp.Scale * (4.0 * t * (1.0 - t));
                var spreadX = 7.5 * sp.Scale * t;
                var r = Math.Max(0.6, (1.45 - t * 0.75) * sp.Scale);

                dc.DrawEllipse(brush, null, new Point(sp.X - spreadX, sp.Y - hopY), r, r);
                dc.DrawEllipse(brush, null, new Point(sp.X + spreadX * 0.9, sp.Y - hopY * 0.82), r * 0.9, r * 0.9);
                dc.DrawEllipse(brush, null, new Point(sp.X + spreadX * 0.15, sp.Y - hopY * 1.15), r * 0.75, r * 0.75);
            }

            // 2. Only 4-5 sliding gravity rivulets + subtle wet condensation trails!
            for (var i = 0; i < rivulets.Count; i++)
            {
                var rv = rivulets[i];
                if (rv.TrailAlpha > 0.12 && rv.Y - rv.TrailTopY > 4)
                {
                    dc.DrawLine(WetTrailPen, new Point(rv.BaseX + rv.W * 0.5, rv.TrailTopY), new Point(rv.X + rv.W * 0.5, rv.Y + rv.H * 0.2));
                }
                if (spriteDropTeardrop != null)
                {
                    dc.DrawImage(spriteDropTeardrop, new Rect(rv.X, rv.Y, rv.W, rv.H));
                }
            }
        }

        // ==================== ONE-TIME 1x OPTICAL SPRITE BAKING ====================
        private static void EnsureOpticalSpritesBaked()
        {
            if (spriteDropTeardrop != null) { return; }

            spriteSunRays = BakeVisual1x(250, 250, delegate(DrawingContext dc)
            {
                var shaftBrush = MakeSoftRadialBrush("#3CFFFBEB", "#00FFE082");
                for (var i = 0; i < 6; i++)
                {
                    dc.PushTransform(new RotateTransform(i * 30.0, 125, 125));
                    dc.DrawEllipse(shaftBrush, null, new Point(125, 125), 16, 120);
                    dc.Pop();
                }
            });

            spriteSunCore = BakeVisual1x(156, 156, delegate(DrawingContext dc)
            {
                var bloom = new RadialGradientBrush();
                bloom.GradientStops.Add(new GradientStop(ColorFrom("#88FFF9C4"), 0.0));
                bloom.GradientStops.Add(new GradientStop(ColorFrom("#3CFDE047"), 0.45));
                bloom.GradientStops.Add(new GradientStop(ColorFrom("#00FFB300"), 1.0));
                bloom.Freeze();
                dc.DrawEllipse(bloom, null, new Point(78, 78), 76, 76);

                var core = new RadialGradientBrush();
                core.GradientOrigin = new Point(0.38, 0.34);
                core.GradientStops.Add(new GradientStop(ColorFrom("#FFFFFFFF"), 0.0));
                core.GradientStops.Add(new GradientStop(ColorFrom("#FFFEF08A"), 0.56));
                core.GradientStops.Add(new GradientStop(ColorFrom("#FFFBBF24"), 1.0));
                core.Freeze();
                dc.DrawEllipse(core, null, new Point(78, 78), 28, 28);
            });

            spriteCloudBright = BakeCloudSprite(false);
            spriteCloudMoody = BakeCloudSprite(true);

            spriteMoonGlow = BakeVisual1x(168, 168, delegate(DrawingContext dc)
            {
                var halo = new RadialGradientBrush();
                halo.GradientStops.Add(new GradientStop(ColorFrom("#48C7D2FE"), 0.0));
                halo.GradientStops.Add(new GradientStop(ColorFrom("#18818CF8"), 0.48));
                halo.GradientStops.Add(new GradientStop(ColorFrom("#00000000"), 1.0));
                halo.Freeze();
                dc.DrawEllipse(halo, null, new Point(84, 84), 80, 80);

                var moonBrush = new LinearGradientBrush(ColorFrom("#FFF8FAFC"), ColorFrom("#FFC7D2FE"), new Point(0, 0), new Point(1, 1));
                moonBrush.Freeze();
                var geo = Geometry.Parse("M 84,60 A 22,22 0 1 0 104,93 A 17.5,17.5 0 1 1 84,60 Z");
                geo.Freeze();
                dc.DrawGeometry(moonBrush, null, geo);
            });

            // Photorealistic organic frosted-glass water droplets (baked with soft calibrated alpha -> zero PushOpacity!)
            spriteDropTeardrop = BakeVisual1x(24, 36, delegate(DrawingContext dc)
            {
                dc.PushTransform(new ScaleTransform(0.75, 0.75));
                var geo = Geometry.Parse("M 16,3 C 19.2,12 27.5,23 27.5,32.5 C 27.5,40.2 22.6,44.2 16,44.2 C 9.4,44.2 4.5,40.2 4.5,32.5 C 4.5,23 12.8,12 16,3 Z");
                geo.Freeze();

                dc.PushTransform(new TranslateTransform(0.8, 2.2));
                var sh = MakeSoftRadialBrush("#2E02060F", "#0002060F");
                dc.DrawEllipse(sh, null, new Point(16, 33), 12.2, 11.5);
                dc.Pop();

                var body = new LinearGradientBrush();
                body.StartPoint = new Point(0.5, 0.0);
                body.EndPoint = new Point(0.5, 1.0);
                body.GradientStops.Add(new GradientStop(ColorFrom("#30071122"), 0.0));
                body.GradientStops.Add(new GradientStop(ColorFrom("#160C1D35"), 0.36));
                body.GradientStops.Add(new GradientStop(ColorFrom("#10FFFFFF"), 0.68));
                body.GradientStops.Add(new GradientStop(ColorFrom("#72E6F4FF"), 0.91));
                body.GradientStops.Add(new GradientStop(ColorFrom("#9AFFFFFF"), 1.0));
                body.Freeze();
                dc.DrawGeometry(body, null, geo);

                dc.PushClip(geo);
                var caustic = MakeSoftRadialBrush("#8AF0F9FF", "#00E0F2FE");
                dc.DrawEllipse(caustic, null, new Point(16.2, 41.2), 9.5, 4.6);
                var spec = MakeSoftRadialBrush("#96FFFFFF", "#00FFFFFF");
                dc.DrawEllipse(spec, null, new Point(12.2, 27.5), 3.6, 5.2);
                dc.Pop();
                dc.Pop();
            });

            spriteDropCling = BakeVisual1x(24, 26, delegate(DrawingContext dc)
            {
                dc.PushTransform(new ScaleTransform(0.75, 0.75));
                var geo = Geometry.Parse("M 16,4.5 C 23.2,4.5 28.2,11.5 28.2,19.2 C 28.2,26.4 23.4,29.8 16,29.8 C 8.6,29.8 3.8,26.4 3.8,19.2 C 3.8,11.5 8.8,4.5 16,4.5 Z");
                geo.Freeze();

                var sh = MakeSoftRadialBrush("#2802060F", "#0002060F");
                dc.DrawEllipse(sh, null, new Point(16.5, 21.5), 12.5, 10.5);

                var body = new LinearGradientBrush();
                body.StartPoint = new Point(0.5, 0.0);
                body.EndPoint = new Point(0.5, 1.0);
                body.GradientStops.Add(new GradientStop(ColorFrom("#30081428"), 0.0));
                body.GradientStops.Add(new GradientStop(ColorFrom("#1210223A"), 0.44));
                body.GradientStops.Add(new GradientStop(ColorFrom("#16FFFFFF"), 0.72));
                body.GradientStops.Add(new GradientStop(ColorFrom("#7EE6F4FF"), 0.96));
                body.Freeze();
                dc.DrawGeometry(body, null, geo);

                dc.PushClip(geo);
                var caustic = MakeSoftRadialBrush("#88F0F9FF", "#00E0F2FE");
                dc.DrawEllipse(caustic, null, new Point(16.0, 27.5), 9.0, 3.8);
                var spec = MakeSoftRadialBrush("#8EFFFFFF", "#00FFFFFF");
                dc.DrawEllipse(spec, null, new Point(11.6, 13.5), 3.5, 2.8);
                dc.Pop();
                dc.Pop();
            });

            spriteDropMicro = BakeVisual1x(12, 12, delegate(DrawingContext dc)
            {
                var body = new RadialGradientBrush();
                body.GradientOrigin = new Point(0.45, 0.72);
                body.Center = new Point(0.5, 0.5);
                body.GradientStops.Add(new GradientStop(ColorFrom("#8CEBF7FF"), 0.0));
                body.GradientStops.Add(new GradientStop(ColorFrom("#2EC8E2F8"), 0.58));
                body.GradientStops.Add(new GradientStop(ColorFrom("#00FFFFFF"), 1.0));
                body.Freeze();
                dc.DrawEllipse(body, null, new Point(6, 6.2), 4.8, 4.6);

                var topDark = MakeSoftRadialBrush("#24081224", "#00081224");
                dc.DrawEllipse(topDark, null, new Point(6, 3.9), 3.1, 2.0);
            });
        }

        private static BitmapSource BakeCloudSprite(bool moody)
        {
            return BakeVisual1x(190, 78, delegate(DrawingContext dc)
            {
                // Pre-baked translucent clouds so rendering needs zero PushOpacity!
                var topColor = moody ? "#3CCBD5E1" : "#58FFFFFF";
                var edgeColor = moody ? "#0064748B" : "#00FFFFFF";
                var b = MakeSoftRadialBrush(topColor, edgeColor);
                dc.DrawEllipse(b, null, new Point(52, 48), 44, 24);
                dc.DrawEllipse(b, null, new Point(95, 36), 52, 32);
                dc.DrawEllipse(b, null, new Point(138, 46), 46, 26);
                dc.DrawEllipse(b, null, new Point(96, 54), 68, 20);
            });
        }

        private static BitmapSource BakeVisual1x(int w, int h, Action<DrawingContext> draw)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                draw(dc);
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        private static Pen MakeFrozenPen(string hex, double thickness)
        {
            var p = new Pen(ThemePalette.FreezeBrush(hex), thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            p.Freeze();
            return p;
        }

        private static RadialGradientBrush MakeSoftRadialBrush(string centerHex, string edgeHex)
        {
            var b = new RadialGradientBrush();
            b.GradientStops.Add(new GradientStop(ColorFrom(centerHex), 0.0));
            b.GradientStops.Add(new GradientStop(ColorFrom(edgeHex), 1.0));
            b.Freeze();
            return b;
        }

        private static Brush MakeHorizontalWetRimBrush()
        {
            var b = new LinearGradientBrush();
            b.StartPoint = new Point(0, 0.5);
            b.EndPoint = new Point(1, 0.5);
            b.GradientStops.Add(new GradientStop(ColorFrom("#00FFFFFF"), 0.0));
            b.GradientStops.Add(new GradientStop(ColorFrom("#58E6F4FF"), 0.25));
            b.GradientStops.Add(new GradientStop(ColorFrom("#78FFFFFF"), 0.5));
            b.GradientStops.Add(new GradientStop(ColorFrom("#48E6F4FF"), 0.78));
            b.GradientStops.Add(new GradientStop(ColorFrom("#00FFFFFF"), 1.0));
            b.Freeze();
            return b;
        }

        private static Brush MakeLightningBrush()
        {
            var b = new RadialGradientBrush();
            b.Center = new Point(0.72, 0.14);
            b.GradientStops.Add(new GradientStop(ColorFrom("#52E0E7FF"), 0.0));
            b.GradientStops.Add(new GradientStop(ColorFrom("#18A5B4FC"), 0.5));
            b.GradientStops.Add(new GradientStop(ColorFrom("#00000000"), 1.0));
            b.Freeze();
            return b;
        }

        private static Color ColorFrom(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
    }
}
