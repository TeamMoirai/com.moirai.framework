using System.Collections;
using Moirai.Atropos;
using UnityEngine;
using UnityEngine.TestTools;

namespace Utility
{
    public partial class TweenEaseTest
    {
        [UnityTest]
        public IEnumerator Test_AnimationCurve()
        {
            var curve = new TweenEase(new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.3f, 1.5f), new Keyframe(1, 0)));
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
            var curve = new TweenEase();
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

                Debug.Log($"InQuadratic[{percent}]： {EaseUtility.InQuadratic(percent) - In_Quadratic(percent)}");
                Debug.Log($"OutQuadratic[{percent}]： {EaseUtility.OutQuadratic(percent) - Out_Quadratic(percent)}");
                Debug.Log($"InOutQuadratic[{percent}]： {EaseUtility.InOutQuadratic(percent) - InOut_Quadratic(percent)}");
                    
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

                Debug.Log($"InCubic[{percent}]： {EaseUtility.InCubic(percent) - In_Cubic(percent)}");
                Debug.Log($"OutCubic[{percent}]： {EaseUtility.OutCubic(percent) - Out_Cubic(percent)}");
                Debug.Log($"InOutCubic[{percent}]： {EaseUtility.InOutCubic(percent) - InOut_Cubic(percent)}");
                    
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

                Debug.Log($"InQuartic[{percent}]： {EaseUtility.InQuartic(percent) - In_Quartic(percent)}");
                Debug.Log($"OutQuartic[{percent}]： {EaseUtility.OutQuartic(percent) - Out_Quartic(percent)}");
                Debug.Log($"InOutQuartic[{percent}]： {EaseUtility.InOutQuartic(percent) - InOut_Quartic(percent)}");
                    
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

                Debug.Log($"InQuintic[{percent}]： {EaseUtility.InQuintic(percent) - In_Quintic(percent)}");
                Debug.Log($"OutQuintic[{percent}]： {EaseUtility.OutQuintic(percent) - Out_Quintic(percent)}");
                Debug.Log($"InOutQuintic[{percent}]： {EaseUtility.InQuintic(percent) - InOut_Quintic(percent)}");
                    
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

                Debug.Log($"InSinusoidal[{percent}]： {EaseUtility.InSinusoidal(percent) - In_Sinusoidal(percent)}");
                Debug.Log($"OutSinusoidal[{percent}]： {EaseUtility.OutSinusoidal(percent) - Out_Sinusoidal(percent)}");
                Debug.Log($"InOutSinusoidal[{percent}]： {EaseUtility.InOutSinusoidal(percent) - InOut_Sinusoidal(percent)}");
                    
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

                Debug.Log($"InBounce[{percent}]： {EaseUtility.InBounce(percent) - In_Bounce(percent)}");
                Debug.Log($"OutBounce[{percent}]： {EaseUtility.OutBounce(percent) - Out_Bounce(percent)}");
                Debug.Log($"InOutBounce[{percent}]： {EaseUtility.InOutBounce(percent) - InOut_Bounce(percent)}");
                    
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

                Debug.Log($"InBack[{percent}]： {EaseUtility.InBack(percent) - In_Overhead(percent)}");
                Debug.Log($"OutBack[{percent}]： {EaseUtility.OutBack(percent) - Out_Overhead(percent)}");
                Debug.Log($"InOutBack[{percent}]： {EaseUtility.InOutBack(percent) - InOut_Overhead(percent)}");
                    
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

                Debug.Log($"InExponential[{percent}]： {EaseUtility.InExponential(percent) - In_Exponential(percent)}");
                Debug.Log($"OutExponential[{percent}]： {EaseUtility.OutExponential(percent) - Out_Exponential(percent)}");
                Debug.Log($"InOutExponential[{percent}]： {EaseUtility.InOutExponential(percent) - InOut_Exponential(percent)}");
                    
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

                Debug.Log($"InElastic [{percent}]： {EaseUtility.InElastic(percent) - In_Elastic(percent)}");
                Debug.Log($"OutElastic [{percent}]： {EaseUtility.OutElastic(percent) - Out_Elastic(percent)}");
                Debug.Log($"InOutElastic [{percent}]： {EaseUtility.InOutElastic(percent) - InOut_Elastic(percent)}");
                    
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

                Debug.Log($"InCircular [{percent}]： {EaseUtility.InCircular(percent) - In_Circular(percent)}");
                Debug.Log($"OutCircular [{percent}]： {EaseUtility.OutCircular(percent) - Out_Circular(percent)}");
                Debug.Log($"InOutCircular [{percent}]： {EaseUtility.InOutCircular(percent) - InOut_Circular(percent)}");
                    
                journey += Time.deltaTime;
                yield return null;
            }
        }
    }
}