using System.Collections;
using Moirai.Atropos;
using UnityEngine;
using UnityEngine.TestTools;

namespace Utility
{
    public class TweenTest
    {
        [UnityTest]
        public IEnumerator Test_AnimationCurve()
        {
            var curve = new Tween(new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.3f, 1.5f), new Keyframe(1, 0)));
            float duration = 0.1f;
            
            for (int i = 0; i < 100; i++)
            {
                float journey = 0f;
                while (journey >= 0 && journey <= duration)
                {
                    float percent = Mathf.Clamp01(journey / duration);
                    curve.Evaluate(percent);
                    
                    journey += Time.deltaTime;
                    yield return null;
                }
                
                yield return null;
            }
        }
        
        [UnityTest]
        public IEnumerator Test_Ease()
        {
            var curve = new Tween();
            float duration = 0.1f;
            
            for (int i = 0; i < 100; i++)
            {
                float journey = 0f;
                while (journey >= 0 && journey <= duration)
                {
                    float percent = Mathf.Clamp01(journey / duration);
                    curve.Evaluate(percent);
                    
                    journey += Time.deltaTime;
                    yield return null;
                }
                
                yield return null;
            }
        }
        
        [UnityTest]
        public IEnumerator Test_Quadratic()
        {
            float duration = 0.1f;

            float journey = 0f;
            while (journey >= 0 && journey <= duration)
            {
                float percent = Mathf.Clamp01(journey / duration);

                Debug.Log($"InQuadratic[{percent}]： {EaseUtility.InQuadratic(percent) - Easing.In_Quadratic(percent)}");
                Debug.Log($"OutQuadratic[{percent}]： {EaseUtility.OutQuadratic(percent) - Easing.Out_Quadratic(percent)}");
                Debug.Log($"InOutQuadratic[{percent}]： {EaseUtility.InOutQuadratic(percent) - Easing.InOut_Quadratic(percent)}");
                    
                journey += Time.deltaTime;
                yield return null;
            }
        }
        
        [UnityTest]
        public IEnumerator Test_Cubic()
        {
            float duration = 0.1f;

            float journey = 0f;
            while (journey >= 0 && journey <= duration)
            {
                float percent = Mathf.Clamp01(journey / duration);

                Debug.Log($"InCubic[{percent}]： {EaseUtility.InCubic(percent) - Easing.In_Cubic(percent)}");
                Debug.Log($"OutCubic[{percent}]： {EaseUtility.OutCubic(percent) - Easing.Out_Cubic(percent)}");
                Debug.Log($"InOutCubic[{percent}]： {EaseUtility.InOutCubic(percent) - Easing.InOut_Cubic(percent)}");
                    
                journey += Time.deltaTime;
                yield return null;
            }
        }
        
        [UnityTest]
        public IEnumerator Test_Quartic()
        {
            float duration = 0.1f;

            float journey = 0f;
            while (journey >= 0 && journey <= duration)
            {
                float percent = Mathf.Clamp01(journey / duration);

                Debug.Log($"InQuartic[{percent}]： {EaseUtility.InQuartic(percent) - Easing.In_Quartic(percent)}");
                Debug.Log($"OutQuartic[{percent}]： {EaseUtility.OutQuartic(percent) - Easing.Out_Quartic(percent)}");
                Debug.Log($"InOutQuartic[{percent}]： {EaseUtility.InOutQuartic(percent) - Easing.InOut_Quartic(percent)}");
                    
                journey += Time.deltaTime;
                yield return null;
            }
        }
        
        
        [UnityTest]
        public IEnumerator Test_Quintic()
        {
            float duration = 0.1f;

            float journey = 0f;
            while (journey >= 0 && journey <= duration)
            {
                float percent = Mathf.Clamp01(journey / duration);

                Debug.Log($"InQuintic[{percent}]： {EaseUtility.InQuintic(percent) - Easing.In_Quintic(percent)}");
                Debug.Log($"OutQuintic[{percent}]： {EaseUtility.OutQuintic(percent) - Easing.Out_Quintic(percent)}");
                Debug.Log($"InOutQuintic[{percent}]： {EaseUtility.InQuintic(percent) - Easing.InOut_Quintic(percent)}");
                    
                journey += Time.deltaTime;
                yield return null;
            }
        }
        
        [UnityTest]
        public IEnumerator Test_Sinusoidal()
        {
            float duration = 0.1f;

            float journey = 0f;
            while (journey >= 0 && journey <= duration)
            {
                float percent = Mathf.Clamp01(journey / duration);

                Debug.Log($"InSinusoidal[{percent}]： {EaseUtility.InSinusoidal(percent) - Easing.In_Sinusoidal(percent)}");
                Debug.Log($"OutSinusoidal[{percent}]： {EaseUtility.OutSinusoidal(percent) - Easing.Out_Sinusoidal(percent)}");
                Debug.Log($"InOutSinusoidal[{percent}]： {EaseUtility.InOutSinusoidal(percent) - Easing.InOut_Sinusoidal(percent)}");
                    
                journey += Time.deltaTime;
                yield return null;
            }
        }
        
        [UnityTest]
        public IEnumerator Test_Bounce()
        {
            float duration = 0.1f;

            float journey = 0f;
            while (journey >= 0 && journey <= duration)
            {
                float percent = Mathf.Clamp01(journey / duration);

                Debug.Log($"InBounce[{percent}]： {EaseUtility.InBounce(percent) - Easing.In_Bounce(percent)}");
                Debug.Log($"OutBounce[{percent}]： {EaseUtility.OutBounce(percent) - Easing.Out_Bounce(percent)}");
                Debug.Log($"InOutBounce[{percent}]： {EaseUtility.InOutBounce(percent) - Easing.InOut_Bounce(percent)}");
                    
                journey += Time.deltaTime;
                yield return null;
            }
        }
        
        [UnityTest]
        public IEnumerator Test_Back()
        {
            float duration = 0.1f;

            float journey = 0f;
            while (journey >= 0 && journey <= duration)
            {
                float percent = Mathf.Clamp01(journey / duration);

                Debug.Log($"InBack[{percent}]： {EaseUtility.InBack(percent) - Easing.In_Overhead(percent)}");
                Debug.Log($"OutBack[{percent}]： {EaseUtility.OutBack(percent) - Easing.Out_Overhead(percent)}");
                Debug.Log($"InOutBack[{percent}]： {EaseUtility.InOutBack(percent) - Easing.InOut_Overhead(percent)}");
                    
                journey += Time.deltaTime;
                yield return null;
            }
        }
        
        [UnityTest]
        public IEnumerator Test_Exponential()
        {
            float duration = 0.1f;

            float journey = 0f;
            while (journey >= 0 && journey <= duration)
            {
                float percent = Mathf.Clamp01(journey / duration);

                Debug.Log($"InExponential[{percent}]： {EaseUtility.InExponential(percent) - Easing.In_Exponential(percent)}");
                Debug.Log($"OutExponential[{percent}]： {EaseUtility.OutExponential(percent) - Easing.Out_Exponential(percent)}");
                Debug.Log($"InOutExponential[{percent}]： {EaseUtility.InOutExponential(percent) - Easing.InOut_Exponential(percent)}");
                    
                journey += Time.deltaTime;
                yield return null;
            }
        }
        
        [UnityTest]
        public IEnumerator Test_Elastic()
        {
            float duration = 0.1f;

            float journey = 0f;
            while (journey >= 0 && journey <= duration)
            {
                float percent = Mathf.Clamp01(journey / duration);

                Debug.Log($"InElastic [{percent}]： {EaseUtility.InElastic(percent) - Easing.In_Elastic(percent)}");
                Debug.Log($"OutElastic [{percent}]： {EaseUtility.OutElastic(percent) - Easing.Out_Elastic(percent)}");
                Debug.Log($"InOutElastic [{percent}]： {EaseUtility.InOutElastic(percent) - Easing.InOut_Elastic(percent)}");
                    
                journey += Time.deltaTime;
                yield return null;
            }
        }
        
        [UnityTest]
        public IEnumerator Test_Circular()
        {
            float duration = 0.1f;

            float journey = 0f;
            while (journey >= 0 && journey <= duration)
            {
                float percent = Mathf.Clamp01(journey / duration);

                Debug.Log($"InCircular [{percent}]： {EaseUtility.InCircular(percent) - Easing.In_Circular(percent)}");
                Debug.Log($"OutCircular [{percent}]： {EaseUtility.OutCircular(percent) - Easing.Out_Circular(percent)}");
                Debug.Log($"InOutCircular [{percent}]： {EaseUtility.InOutCircular(percent) - Easing.InOut_Circular(percent)}");
                    
                journey += Time.deltaTime;
                yield return null;
            }
        }
    }
}