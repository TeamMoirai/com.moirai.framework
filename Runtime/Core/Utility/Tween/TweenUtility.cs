using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 缓动动画相关的实用函数。
    /// </summary>
    public static partial class TweenUtility
    {
        private static ITweenHelper s_TweenHelper = null;

        /// <summary>
        /// 设置动画处理器。
        /// </summary>
        /// <param name="textHelper">要设置的动画处理器。</param>
        public static void SetTweenHelper(ITweenHelper textHelper)
        {
            s_TweenHelper = textHelper;
        }

        public static bool IsTweening(object onTarget)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.IsTweening(onTarget);
        }

        public static int GetTweenCount(object onTarget)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.GetTweenCount(onTarget);
        }

        public static bool IsAlive(long tweenId)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.IsAlive(tweenId);
        }

        public static void Stop(long tweenId)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            s_TweenHelper.Stop(tweenId);
        }

        public static void Complete(long tweenId)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            s_TweenHelper.Complete(tweenId);
        }

        public static int StopAll(object onTarget = null)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.StopAll(onTarget);
        }

        public static int CompleteAll(object onTarget = null)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.CompleteAll(onTarget);
        }

        public static void OnComplete(long tweenId, Action onComplete)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            s_TweenHelper.OnComplete(tweenId, onComplete);
        }

        public static long Delay(float duration, Action onComplete = null, bool useUnscaledTime = false, bool warnIfTargetDestroyed = true)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Delay(duration, onComplete, useUnscaledTime, warnIfTargetDestroyed);
        }

        public static long Delay(object target, float duration, Action onComplete = null, bool useUnscaledTime = false, bool warnIfTargetDestroyed = true)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Delay(target, duration, onComplete, useUnscaledTime, warnIfTargetDestroyed);
        }

        public static long LocalRotation(UnityEngine.Transform target, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.LocalRotation(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long LocalRotation(UnityEngine.Transform target, UnityEngine.Vector3 startValue, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default,
            int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.LocalRotation(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long Scale(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Scale(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long Scale(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Scale(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long Rotation(UnityEngine.Transform target, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Rotation(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long Rotation(UnityEngine.Transform target, UnityEngine.Vector3 startValue, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Rotation(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long Position(UnityEngine.Transform target, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Position(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long Position(UnityEngine.Transform target, UnityEngine.Vector3 startValue, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Position(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long PositionX(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.PositionX(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long PositionX(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.PositionX(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long PositionY(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.PositionY(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long PositionY(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.PositionY(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long PositionZ(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.PositionZ(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long PositionZ(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.PositionZ(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long LocalPosition(UnityEngine.Transform target, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.LocalPosition(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long LocalPosition(UnityEngine.Transform target, UnityEngine.Vector3 startValue, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default,
            int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.LocalPosition(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long LocalPositionX(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.LocalPositionX(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long LocalPositionX(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.LocalPositionX(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long LocalPositionY(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.LocalPositionY(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long LocalPositionY(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.LocalPositionY(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long LocalPositionZ(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.LocalPositionZ(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long LocalPositionZ(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.LocalPositionZ(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long Rotation(UnityEngine.Transform target, UnityEngine.Quaternion endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Rotation(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long Rotation(UnityEngine.Transform target, UnityEngine.Quaternion startValue, UnityEngine.Quaternion endValue, float duration, Ease ease = Ease.Default,
            int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Rotation(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long LocalRotation(UnityEngine.Transform target, UnityEngine.Quaternion endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.LocalRotation(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long LocalRotation(UnityEngine.Transform target, UnityEngine.Quaternion startValue, UnityEngine.Quaternion endValue, float duration, Ease ease = Ease.Default,
            int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.LocalRotation(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long Scale(UnityEngine.Transform target, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Scale(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long Scale(UnityEngine.Transform target, UnityEngine.Vector3 startValue, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Scale(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long ScaleX(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.ScaleX(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long ScaleX(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.ScaleX(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long ScaleY(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.ScaleY(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long ScaleY(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.ScaleY(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long ScaleZ(UnityEngine.Transform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.ScaleZ(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long ScaleZ(UnityEngine.Transform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.ScaleZ(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long Color(UnityEngine.SpriteRenderer target, UnityEngine.Color endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Color(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long Color(UnityEngine.SpriteRenderer target, UnityEngine.Color startValue, UnityEngine.Color endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Color(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long MaterialColor(UnityEngine.Material target, UnityEngine.Color startValue, UnityEngine.Color endValue, float duration, Ease ease = Ease.Default, int
                cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.MaterialColor(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }
        public static long Alpha(UnityEngine.SpriteRenderer target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Alpha(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long Alpha(UnityEngine.SpriteRenderer target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Alpha(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long UISliderValue(UnityEngine.UI.Slider target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.UISliderValue(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long UISliderValue(UnityEngine.UI.Slider target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.UISliderValue(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long UINormalizedPosition(UnityEngine.UI.ScrollRect target, UnityEngine.Vector2 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.UINormalizedPosition(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long UINormalizedPosition(UnityEngine.UI.ScrollRect target, UnityEngine.Vector2 startValue, UnityEngine.Vector2 endValue, float duration, Ease ease = Ease.Default,
            int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.UINormalizedPosition(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long UIHorizontalNormalizedPosition(UnityEngine.UI.ScrollRect target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.UIHorizontalNormalizedPosition(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long UIHorizontalNormalizedPosition(UnityEngine.UI.ScrollRect target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.UIHorizontalNormalizedPosition(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long UIAnchoredPosition(UnityEngine.RectTransform target, UnityEngine.Vector2 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.UIAnchoredPosition(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long UIAnchoredPosition(UnityEngine.RectTransform target, UnityEngine.Vector2 startValue, UnityEngine.Vector2 endValue, float duration, Ease ease = Ease.Default,
            int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.UIAnchoredPosition(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long UIAnchoredPositionX(UnityEngine.RectTransform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.UIAnchoredPositionX(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long UIAnchoredPositionX(UnityEngine.RectTransform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.UIAnchoredPositionX(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long UIAnchoredPositionY(UnityEngine.RectTransform target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.UIAnchoredPositionY(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long UIAnchoredPositionY(UnityEngine.RectTransform target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.UIAnchoredPositionY(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long UIVerticalNormalizedPosition(UnityEngine.UI.ScrollRect target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.UIVerticalNormalizedPosition(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long UIVerticalNormalizedPosition(UnityEngine.UI.ScrollRect target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.UIVerticalNormalizedPosition(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long UIAnchoredPosition3D(UnityEngine.RectTransform target, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.UIAnchoredPosition3D(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long UIAnchoredPosition3D(UnityEngine.RectTransform target, UnityEngine.Vector3 startValue, UnityEngine.Vector3 endValue, float duration, Ease ease = Ease.Default,
            int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.UIAnchoredPosition3D(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long UISizeDelta(UnityEngine.RectTransform target, UnityEngine.Vector2 endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.UISizeDelta(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long UISizeDelta(UnityEngine.RectTransform target, UnityEngine.Vector2 startValue, UnityEngine.Vector2 endValue, float duration, Ease ease = Ease.Default,
            int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.UISizeDelta(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long Color(UnityEngine.UI.Graphic target, UnityEngine.Color endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Color(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long Color(UnityEngine.UI.Graphic target, UnityEngine.Color startValue, UnityEngine.Color endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Color(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long Alpha(UnityEngine.CanvasGroup target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Alpha(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long Alpha(UnityEngine.CanvasGroup target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Alpha(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long Alpha(UnityEngine.UI.Graphic target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Alpha(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long Alpha(UnityEngine.UI.Graphic target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Alpha(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }


        public static long UIFillAmount(UnityEngine.UI.Image target, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart,
            float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.UIFillAmount(target, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long UIFillAmount(UnityEngine.UI.Image target, Single startValue, Single endValue, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.UIFillAmount(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long MoveBezierPath(UnityEngine.Transform target, UnityEngine.Vector3[] path, float duration, Ease ease = Ease.Default, int cycles = 1,
            CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.MoveBezierPath(target, path, duration, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long Custom<T>(T target, UnityEngine.Vector3 startValue, UnityEngine.Vector3 endValue, float duration, Action<T, UnityEngine.Vector3> onValueChange,
            Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            where T : class
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Custom(target, startValue, endValue, duration, onValueChange, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long Custom<T>(T target, long startValue, long endValue, float duration, Action<T, long> onValueChange,
            Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            where T : class
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Custom(target, startValue, endValue, duration, onValueChange, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }

        public static long Custom<T>(T target, float startValue, float endValue, float duration, Action<T, float> onValueChange,
            Ease ease = Ease.Default, int cycles = 1, CycleMode cycleMode = CycleMode.Restart, float startDelay = 0, float endDelay = 0, bool useUnscaledTime = false)
            where T : class
        {
            if (s_TweenHelper == null)
            {
                throw new GameException("ITweenHelper is invalid.");
            }
            return s_TweenHelper.Custom(target, startValue, endValue, duration, onValueChange, ease, cycles, cycleMode, startDelay, endDelay, useUnscaledTime);
        }
    }
    
    public static class TweenExtensions
    {
        public static long OnComplete(this long tweenId, Action onComplete)
        {
            TweenUtility.OnComplete(tweenId, onComplete);
            return tweenId;
        }
    }
}