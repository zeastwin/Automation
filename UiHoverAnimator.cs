using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Automation
{
    /// <summary>
    /// 为导航和工具栏提供克制的悬停渐变与按压反馈。
    /// </summary>
    internal sealed class UiHoverAnimator : IDisposable
    {
        private sealed class AnimationState
        {
            public Control Control;
            public Func<Color> RestingColor;
            public Color HoverColor;
            public float Progress;
            public float Target;
            public bool PressOffset;
            public Padding OriginalPadding;
        }

        private readonly Timer timer = new Timer { Interval = 15 };
        private readonly List<AnimationState> states = new List<AnimationState>();

        public UiHoverAnimator()
        {
            timer.Tick += Timer_Tick;
        }

        public void Attach(Control control, Func<Color> restingColor, Color hoverColor, bool pressOffset)
        {
            AnimationState state = new AnimationState
            {
                Control = control,
                RestingColor = restingColor,
                HoverColor = hoverColor,
                PressOffset = pressOffset,
                OriginalPadding = control.Padding
            };
            states.Add(state);
            control.MouseEnter += (sender, args) => SetTarget(state, 1F);
            control.MouseLeave += (sender, args) =>
            {
                RestorePadding(state);
                SetTarget(state, 0F);
            };
            control.MouseDown += (sender, args) =>
            {
                if (state.PressOffset && args.Button == MouseButtons.Left)
                {
                    control.Padding = new Padding(
                        state.OriginalPadding.Left,
                        state.OriginalPadding.Top + 1,
                        state.OriginalPadding.Right,
                        Math.Max(0, state.OriginalPadding.Bottom - 1));
                }
            };
            control.MouseUp += (sender, args) => RestorePadding(state);
        }

        public void RefreshRestingColors()
        {
            foreach (AnimationState state in states)
            {
                if (state.Progress <= 0.001F && !state.Control.IsDisposed)
                {
                    ApplyColor(state, state.RestingColor());
                }
            }
        }

        private void SetTarget(AnimationState state, float target)
        {
            state.Target = target;
            if (!timer.Enabled)
            {
                timer.Start();
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            bool animating = false;
            foreach (AnimationState state in states)
            {
                if (state.Control.IsDisposed)
                {
                    continue;
                }
                float difference = state.Target - state.Progress;
                if (Math.Abs(difference) > 0.01F)
                {
                    state.Progress += difference * 0.18F;
                    animating = true;
                }
                else
                {
                    state.Progress = state.Target;
                }
                ApplyColor(state, Blend(state.RestingColor(), state.HoverColor, state.Progress));
            }
            if (!animating)
            {
                timer.Stop();
            }
        }

        private static void ApplyColor(AnimationState state, Color color)
        {
            state.Control.BackColor = color;
            if (state.Control is Button button)
            {
                button.FlatAppearance.MouseOverBackColor = color;
                button.FlatAppearance.MouseDownBackColor = color;
            }
        }

        private static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Max(0F, Math.Min(1F, amount));
            return Color.FromArgb(
                (int)(from.A + (to.A - from.A) * amount),
                (int)(from.R + (to.R - from.R) * amount),
                (int)(from.G + (to.G - from.G) * amount),
                (int)(from.B + (to.B - from.B) * amount));
        }

        private static void RestorePadding(AnimationState state)
        {
            if (!state.Control.IsDisposed)
            {
                state.Control.Padding = state.OriginalPadding;
            }
        }

        public void Dispose()
        {
            timer.Stop();
            timer.Dispose();
            states.Clear();
        }
    }
}
