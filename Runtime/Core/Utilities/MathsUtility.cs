using UnityEngine;

namespace Moirai.Atropos
{
	/// <summary>
	/// Math helpers
	/// </summary>
	public static partial class MathsUtility
    {
        /// <summary>
        /// 计算弹簧速度的内部方法
        /// </summary>
        /// <param name="currentValue"></param>
        /// <param name="targetValue"></param>
        /// <param name="velocity"></param>
        /// <param name="damping"></param>
        /// <param name="frequency"></param>
        /// <param name="deltaTime"></param>
        /// <returns></returns>
        private static float SpringVelocity(float currentValue, float targetValue, float velocity, float damping, float frequency, float deltaTime)
        {
	        frequency = frequency * 2f * Mathf.PI;
	        float f2 = frequency * frequency;
	        float d2 = 2.0f * damping * frequency;
	        float x = currentValue - targetValue;
	        float acceleration = -f2 * x - d2 * velocity;
	        velocity += deltaTime * acceleration;
	        return velocity;
        }
        

        /// <summary>
        /// 将 float 弹向目标值（类似弹簧效果） 
        /// </summary>
        /// <param name="currentValue">当前值，作为 ref 传入</param>
        /// <param name="targetValue">目标值</param>
        /// <param name="velocity">速度值，作为 ref 传入，用于计算弹簧值的当前速度</param>
        /// <param name="damping">阻尼，在0.01F和1F之间，阻尼越高，弹性越差</param>
        /// <param name="frequency">频率，以 Hz 为单位，弹簧在 1 秒内应经过的周期数</param>
        /// <param name="deltaTime">增量时间（通常为 Time.deltaTime 或 Time.unscaledDeltaTime）</param>
        public static void Spring(ref float currentValue, float targetValue, ref float velocity, float damping, float frequency, float deltaTime)
        {
	        float fixedDeltaTime = 1.0f / 60.0f; 
	        float accumulator = deltaTime;
	        while (accumulator > 0f)
	        {
		        float step = Mathf.Min(accumulator, fixedDeltaTime);
		        velocity = SpringVelocity(currentValue, targetValue, velocity, damping, frequency, step);
		        currentValue += step * velocity;
		        accumulator -= step;
	        }
        }

        /// <summary>
        /// 将 Vector2 弹向目标值（类似弹簧效果）
        /// </summary>
        /// <param name="currentValue">当前值，作为 ref 传入</param>
        /// <param name="targetValue">目标值</param>
        /// <param name="velocity">速度值，作为 ref 传入，用于计算弹簧值的当前速度</param>
        /// <param name="damping">阻尼，在0.01F和1F之间，阻尼越高，弹性越差</param>
        /// <param name="frequency">频率，以 Hz 为单位，弹簧在 1 秒内应经过的周期数</param>
        /// <param name="deltaTime">增量时间（通常为 Time.deltaTime 或 Time.unscaledDeltaTime）</param>
        public static void Spring(ref Vector2 currentValue, Vector2 targetValue, ref Vector2 velocity, float damping, float frequency, float deltaTime)
        {
	        float fixedDeltaTime = 1.0f / 60.0f; 
	        float accumulator = deltaTime;
	        while (accumulator > 0f)
	        {
		        float step = Mathf.Min(accumulator, fixedDeltaTime);
		        velocity.x = SpringVelocity(currentValue.x, targetValue.x, velocity.x, damping, frequency, step);
		        velocity.y = SpringVelocity(currentValue.y, targetValue.y, velocity.y, damping, frequency, step);
		        currentValue += step * velocity;
		        accumulator -= step;
	        }
        }

        /// <summary>
        /// 将 Vector3 弹向目标值（类似弹簧效果）
        /// </summary>
        /// <param name="currentValue">当前值，作为 ref 传入</param>
        /// <param name="targetValue">目标值</param>
        /// <param name="velocity">速度值，作为 ref 传入，用于计算弹簧值的当前速度</param>
        /// <param name="damping">阻尼，在0.01F和1F之间，阻尼越高，弹性越差</param>
        /// <param name="frequency">频率，以 Hz 为单位，弹簧在 1 秒内应经过的周期数</param>
        /// <param name="deltaTime">增量时间（通常为 Time.deltaTime 或 Time.unscaledDeltaTime）</param>
        public static void Spring(ref Vector3 currentValue, Vector3 targetValue, ref Vector3 velocity, float damping, float frequency, float deltaTime)
        {
	        float fixedDeltaTime = 1.0f / 60.0f; 
	        float accumulator = deltaTime;
	        while (accumulator > 0f)
	        {
		        float step = Mathf.Min(accumulator, fixedDeltaTime);
		        velocity.x = SpringVelocity(currentValue.x, targetValue.x, velocity.x, damping, frequency, step);
		        velocity.y = SpringVelocity(currentValue.y, targetValue.y, velocity.y, damping, frequency, step);
		        velocity.z = SpringVelocity(currentValue.z, targetValue.z, velocity.z, damping, frequency, step);
		        currentValue += step * velocity;
		        accumulator -= step;
	        }
        }

        /// <summary>
        /// 将 Vector4 弹向目标值（类似弹簧效果）
        /// </summary>
        /// <param name="currentValue">当前值，作为 ref 传入</param>
        /// <param name="targetValue">目标值</param>
        /// <param name="velocity">速度值，作为 ref 传入，用于计算弹簧值的当前速度</param>
        /// <param name="damping">阻尼，在0.01F和1F之间，阻尼越高，弹性越差</param>
        /// <param name="frequency">频率，以 Hz 为单位，弹簧在 1 秒内应经过的周期数</param>
        /// <param name="deltaTime">增量时间（通常为 Time.deltaTime 或 Time.unscaledDeltaTime）</param>
        public static void Spring(ref Vector4 currentValue, Vector4 targetValue, ref Vector4 velocity, float damping, float frequency, float deltaTime)
        {
	        float fixedDeltaTime = 1.0f / 60.0f; 
	        float accumulator = deltaTime;
	        while (accumulator > 0f)
	        {
		        float step = Mathf.Min(accumulator, fixedDeltaTime);
		        velocity.x = SpringVelocity(currentValue.x, targetValue.x, velocity.x, damping, frequency, step);
		        velocity.y = SpringVelocity(currentValue.y, targetValue.y, velocity.y, damping, frequency, step);
		        velocity.z = SpringVelocity(currentValue.z, targetValue.z, velocity.z, damping, frequency, step);
		        velocity.w = SpringVelocity(currentValue.w, targetValue.w, velocity.w, damping, frequency, step);
		        currentValue += step * velocity; 
		        accumulator -= step;
	        }
        }

        /// <summary>
        /// 计算插值速率的内部方法
        /// </summary>
        /// <param name="rate"></param>
        /// <returns></returns>
        private static float LerpRate(float rate, float deltaTime)
        {
            rate = Mathf.Clamp01(rate);
            float invRate = - Mathf.Log(1.0f - rate, 2.0f) * 60f;
            return Mathf.Pow(2.0f, -invRate * deltaTime);
        }

        /// <summary>
        /// 以指定速率向目标 float 插值
        /// </summary>
        /// <param name="value"></param>
        /// <param name="target"></param>
        /// <param name="rate"></param>
        /// <returns></returns>
        public static float Lerp(float value, float target, float rate, float deltaTime)
        {
            if (deltaTime == 0f) { return value; }
            return Mathf.Lerp(target, value, LerpRate(rate, deltaTime));
        }

        /// <summary>
        /// 以指定速率向目标 Vector2 插值
        /// </summary>
        /// <param name="value"></param>
        /// <param name="target"></param>
        /// <param name="rate"></param>
        /// <returns></returns>
        public static Vector2 Lerp(Vector2 value, Vector2 target, float rate, float deltaTime)
        {
            if (deltaTime == 0f) { return value; }
            return Vector2.Lerp(target, value, LerpRate(rate, deltaTime));
        }

        /// <summary>
        /// 以指定速率向目标 Vector3 插值
        /// </summary>
        /// <param name="value"></param>
        /// <param name="target"></param>
        /// <param name="rate"></param>
        /// <returns></returns>
        public static Vector3 Lerp(Vector3 value, Vector3 target, float rate, float deltaTime)
        {
            if (deltaTime == 0f) { return value; }
            return Vector3.Lerp(target, value, LerpRate(rate, deltaTime));
        }

        /// <summary>
        /// 以指定速率向目标 Vector4 插值
        /// </summary>
        /// <param name="value"></param>
        /// <param name="target"></param>
        /// <param name="rate"></param>
        /// <returns></returns>
        public static Vector4 Lerp(Vector4 value, Vector4 target, float rate, float deltaTime)
        {
            if (deltaTime == 0f) { return value; }
            return Vector4.Lerp(target, value, LerpRate(rate, deltaTime));
        }

        /// <summary>
        /// 以指定速率向目标 Quaternion 插值
        /// </summary>
        /// <param name="value"></param>
        /// <param name="target"></param>
        /// <param name="rate"></param>
        /// <returns></returns>
        public static Quaternion Lerp(Quaternion value, Quaternion target, float rate, float deltaTime)
        {
            if (deltaTime == 0f) { return value; }
            return Quaternion.Lerp(target, value, LerpRate(rate, deltaTime));
        }

        /// <summary>
        /// 以指定速率向目标 Color 插值
        /// </summary>
        /// <param name="value"></param>
        /// <param name="target"></param>
        /// <param name="rate"></param>
        /// <returns></returns>
        public static Color Lerp(Color value, Color target, float rate, float deltaTime)
        {
            if (deltaTime == 0f) { return value; }
            return Color.Lerp(target, value, LerpRate(rate, deltaTime));
        }

        /// <summary>
        /// 以指定速率向目标 Color32 插值
        /// </summary>
        /// <param name="value"></param>
        /// <param name="target"></param>
        /// <param name="rate"></param>
        /// <returns></returns>
        public static Color32 Lerp(Color32 value, Color32 target, float rate, float deltaTime)
        {
            if (deltaTime == 0f) { return value; }
            return Color32.Lerp(target, value, LerpRate(rate, deltaTime));
        }

        /// <summary>
        /// 将值限制在 min 和 max 之间，两个边界都是可选的，分别由 clampMin 和 clampMax 决定
        /// </summary>
        /// <param name="value"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <param name="clampMin"></param>
        /// <param name="clampMax"></param>
        /// <returns></returns>
        public static float Clamp(float value, float min, float max, bool clampMin, bool clampMax)
        {
            float returnValue = value;
            if (clampMin && (returnValue < min))
            {
                returnValue = min;
            }
            if (clampMax && (returnValue > max))
            {
                returnValue = max;
            }
            return returnValue;
        }

        /// <summary>
        /// 将浮点数四舍五入到最接近的半值：1、1.5、2、2.5 等
        /// </summary>
        /// <param name="a"></param>
        /// <returns></returns>
        public static float RoundToNearestHalf(float a)
        {
            return a = a - (a % 0.5f);
        }

        /// <summary>
        /// 转向目标（2D）
        /// </summary>
        /// <param name="direction"></param>
        /// <returns></returns>
        public static Quaternion LookAt2D(Vector2 direction)
        {
	        var angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
	        return Quaternion.AngleAxis(angle, Vector3.forward);
        }
        
        /// <summary>
        /// 将 Vector3 转换为 Vector2
        /// </summary>
        /// <returns></returns>
        /// <param name="target">要转换的 Vector3</param>
        public static Vector2 Vector3ToVector2(Vector3 target) 
		{
			return new Vector2(target.x, target.y);
		}

		/// <summary>
		/// 将 Vector2 转换为 z 为 0 的 Vector3
		/// </summary>
		/// <returns></returns>
		/// <param name="target">要转换的 Vector2</param>
		public static Vector3 Vector2ToVector3(Vector2 target) 
		{
			return new Vector3(target.x, target.y, 0);
		}

		/// <summary>
		/// 将 Vector2 转换为指定 z 的 Vector3
		/// </summary>
		/// <returns></returns>
		/// <param name="target">要转换的 Vector2</param>
		/// <param name="newZValue">新的 Z 值</param>
		public static Vector3 Vector2ToVector3(Vector2 target, float newZValue) 
		{
			return new Vector3(target.x, target.y, newZValue);
		}

		/// <summary>
		/// 将 Vector3 的所有值进行四舍五入
		/// </summary>
		/// <returns>.</returns>
		/// <param name="vector"></param>
		public static Vector3 RoundVector3(Vector3 vector)
		{
			return new Vector3(Mathf.Round(vector.x), Mathf.Round(vector.y), Mathf.Round(vector.z));
        }

        /// <summary>
        /// 从 2 个定义的 Vector2 返回一个随机 Vector2。
        /// </summary>
        /// <returns></returns>
        /// <param name="minimum">x，y 的最小值</param>
        /// <param name="maximum">x，y 的最大值</param>
        public static Vector2 RandomVector2(Vector2 minimum, Vector2 maximum)
        {
            return new Vector2(UnityEngine.Random.Range(minimum.x, maximum.x),
                                             UnityEngine.Random.Range(minimum.y, maximum.y));
        }

        /// <summary>
        /// 从 2 个定义的 Vector3 返回一个随机 Vector3。
        /// </summary>
        /// <returns></returns>
        /// <param name="minimum">x，y，z 的最小值</param>
        /// <param name="maximum">x，y，z 的最大值</param>
        public static Vector3 RandomVector3(Vector3 minimum, Vector3 maximum)
        {
            return new Vector3(UnityEngine.Random.Range(minimum.x, maximum.x),
                                             UnityEngine.Random.Range(minimum.y, maximum.y),
                                             UnityEngine.Random.Range(minimum.z, maximum.z));
        }

        /// <summary>
        /// 返回指定半径圆上的随机点
        /// </summary>
        /// <param name="circleRadius"></param>
        /// <returns></returns>
        public static Vector2 RandomPointOnCircle(float circleRadius)
        {
	        return UnityEngine.Random.insideUnitCircle.normalized * circleRadius;
        }

        /// <summary>
        /// 返回指定半径球面上的随机点
        /// </summary>
        /// <param name="sphereRadius"></param>
        /// <returns></returns>
        public static Vector3 RandomPointOnSphere(float sphereRadius)
        {
	        return UnityEngine.Random.onUnitSphere * sphereRadius;
        }

        /// <summary>
        /// 将点围绕给定中心点旋转指定角度。
        /// </summary>
        /// <returns>旋转后点的位置</returns>
        /// <param name="point">要旋转的点</param>
        /// <param name="pivot">中心点</param>
        /// <param name="angle">要旋转的角度</param>
        public static Vector3 RotatePointAroundPivot(Vector3 point, Vector3 pivot, float angle) 
		{			
			angle = angle * (Mathf.PI / 180f);
            float rotatedX = Mathf.Cos(angle) * (point.x - pivot.x) - Mathf.Sin(angle) * (point.y-pivot.y) + pivot.x;
            float rotatedY = Mathf.Sin(angle) * (point.x - pivot.x) + Mathf.Cos(angle) * (point.y - pivot.y) + pivot.y;
			return new Vector3(rotatedX,rotatedY,0);		
	    }

		/// <summary>
		/// 将点围绕给定中心点旋转指定角度。
		/// </summary>
		/// <returns>旋转后点的位置</returns>
		/// <param name="point">要旋转的点</param>
		/// <param name="pivot">中心点</param>
		/// <param name="angle">要旋转的 Vector3 角度</param>
		public static Vector3 RotatePointAroundPivot(Vector3 point, Vector3 pivot, Vector3 angle) 
		{
			// 计算从点到中心点的点方向
		   	Vector3 direction = point - pivot; 
		   	// 旋转方向
		   	direction = Quaternion.Euler(angle) * direction; 
		   	// 计算旋转点的位置
		   	point = direction + pivot; 
		   	return point; 
		}

		/// <summary>
		/// 将点围绕给定中心点旋转指定角度。
		/// </summary>
		/// <returns>旋转后点的位置</returns>
		/// <param name="point">要旋转的点</param>
		/// <param name="pivot">中心点</param>
		/// <param name="quaternion">要旋转的四元数角度</param>
		public static Vector3 RotatePointAroundPivot(Vector3 point, Vector3 pivot, Quaternion quaternion) 
		{
			// 计算从点到中心点的点方向
		   	Vector3 direction = point - pivot; 
		    // 旋转方向
		   	direction = quaternion * direction; 
		    // 计算旋转点的位置
		   	point = direction + pivot; 
		   	return point; 
		 }

		/// <summary>
		/// 将 vector2 旋转指定的角度（以度为单位）并返回
		/// </summary>
		/// <returns>旋转后的 Vector2</returns>
		/// <param name="vector">要旋转的 Vector2</param>
		/// <param name="angle">旋转角度</param>
		public static Vector2 RotateVector2(Vector2 vector, float angle)
		{
			if (angle == 0)
			{
				return vector;
			}
			float sinus = Mathf.Sin(angle * Mathf.Deg2Rad);
			float cosinus = Mathf.Cos(angle * Mathf.Deg2Rad);

			float oldX = vector.x;
			float oldY = vector.y;
			vector.x = (cosinus * oldX) - (sinus * oldY);
			vector.y = (sinus * oldX) + (cosinus * oldY);
			return vector;
		}

		/// <summary>
		/// 计算两个二维向量之间的角度。
		/// </summary>
		/// <returns></returns>
		/// <param name="vectorA">Vector a</param>
		/// <param name="vectorB">Vector b</param>
		public static float AngleBetween(Vector2 vectorA, Vector2 vectorB)
		{
			float angle = Vector2.Angle(vectorA, vectorB);
			Vector3 cross = Vector3.Cross(vectorA, vectorB);

			if (cross.z > 0)
			{
				angle = 360 - angle;
			}

			return angle;
		}

		/// <summary>
		/// 计算并返回两个向量之间的方向，用于检查一个向量是指向另一个向量的左边还是右边
		/// </summary>
		/// <returns>-1：方向相反，0：彼此垂直，1：方向相同</returns>
		/// <param name="vectorA">Vector a</param>
		/// <param name="vectorB">Vector b</param>
		public static float AngleDirection(Vector3 vectorA, Vector3 vectorB, Vector3 up)
		{
			Vector3 cross = Vector3.Cross(vectorA, vectorB);
			float direction = Vector3.Dot(cross, up);

			return direction;
		}

		/// <summary>
		/// 返回点和线之间的距离
		/// </summary>
		/// <returns></returns>
		/// <param name="point">点</param>
		/// <param name="lineStart">线的开始</param>
		/// <param name="lineEnd">线的结束</param>
		public static float DistanceBetweenPointAndLine(Vector3 point, Vector3 lineStart, Vector3 lineEnd)
		{
			return Vector3.Magnitude(ProjectPointOnLine(point, lineStart, lineEnd) - point);
		}

		/// <summary>
		/// 在直线上投影一个点（垂直）并返回投影点
		/// </summary>
		/// <returns>在线上的点</returns>
		/// <param name="point">点</param>
		/// <param name="lineStart">线的开始</param>
		/// <param name="lineEnd">线的结束</param>
		public static Vector3 ProjectPointOnLine(Vector3 point, Vector3 lineStart, Vector3 lineEnd)
		{
			Vector3 rhs = point - lineStart;
			Vector3 vector2 = lineEnd - lineStart;
			float magnitude = vector2.magnitude;
			Vector3 lhs = vector2;
			if (magnitude > 1E-06f)
			{
				lhs = (Vector3)(lhs / magnitude);
			}
			float num2 = Mathf.Clamp(Vector3.Dot(lhs, rhs), 0f, magnitude);
			return (lineStart + ((Vector3)(lhs * num2)));
		}

		/// <summary>
		/// 返回传入参数的所有 int 的总和
		/// </summary>
		/// <param name="thingsToAdd">要相加的值</param>
		public static int Sum(params int[] thingsToAdd)
		{
			int result = 0;
			for (int i = 0; i < thingsToAdd.Length; i++)
			{
				result += thingsToAdd[i];
			}
			return result;
		}

		/// <summary>
		/// 返回掷骰子的结果，即 1~N 之间随机
		/// </summary>
		/// <returns>返回掷骰子的结果</returns>
		/// <param name="numberOfSides">骰子的面数</param>
		public static int RollADice(int numberOfSides)
		{
			return (UnityEngine.Random.Range(1, numberOfSides + 1));
		}

		/// <summary>
		/// X% 的机会返回随机成功。
		/// <example>有 20% 的机会，Chance(20) > true</example>>
		/// </summary>
		/// <param name="percent">几率的百分比</param>
		public static bool Chance(int percent)
		{
			return (UnityEngine.Random.Range(0, 100) <= percent);
		}

		/// <summary>
		/// 从“from”移动到“to”的指定量，并返回加值结果
		/// </summary>
		/// <param name="from">A 点</param>
		/// <param name="to">B 点</param>
		/// <param name="amount">加值</param>
		public static float Approach(float from, float to, float amount)
		{
			if (Mathf.Approximately(from, to))
			{
				return from;
			}
			
			if (from < to)
			{
				from += amount;
				if (from > to)
				{
					return to;
				}
			}
			
			if (from > to)
			{
				from -= amount;
				if (from < to)
				{
					return to;
				}
			}
			
			return from;
		}
		
		
		/// <summary>
		/// 将区间 [A，B] 中的值 x 重新映射到区间 [C，D] 中的值（所占各自区间的比例相同）
		/// </summary>
		/// <param name="x">要重新映射的值</param>
		/// <param name="A">包含 x 值的区间 [A，B] 的最小边界</param>
		/// <param name="B">包含 x 值的区间 [A，B] 的最大边界</param>
		/// <param name="C">目标区间 [C，D] 的最小边界</param>
		/// <param name="D">目标区间 [C，D] 的最大边界</param>
		public static float Remap(float x, float A, float B, float C, float D)
		{
			float remappedValue = C + (x - A) / (B - A) * (D - C);
			return remappedValue;
		}

        /// <summary>
        /// 将角度限制在最小角度和最大角度之间（所有角度均以度表示）
        /// </summary>
        /// <param name="angle"></param>
        /// <param name="minimumAngle"></param>
        /// <param name="maximumAngle"></param>
        /// <returns></returns>
        public static float ClampAngle(float angle, float minimumAngle, float maximumAngle)
        {
            if (angle < -360)
            {
                angle += 360;
            }
            if (angle > 360)
            {
                angle -= 360;
            }
            return Mathf.Clamp(angle, minimumAngle, maximumAngle);
        }

        public static float RoundToDecimal(float value, int numberOfDecimals)
        {
	        if (numberOfDecimals <= 0)
	        {
		        return Mathf.Round(value);
	        }
	        else
	        {
		        return Mathf.Round(value * 10f * numberOfDecimals) / (10f * numberOfDecimals);
	        }
        }

        /// <summary>
        /// 将传入参数的值四舍五入到参数数组中最接近的值
        /// </summary>
        /// <param name="value"></param>
        /// <param name="possibleValues"></param>
        /// <returns></returns>
        public static float RoundToClosest(float value, float[] possibleValues, bool pickSmallestDistance = false)
		{
			if (possibleValues.Length == 0) 
			{
				return 0f;
			}

			float closestValue = possibleValues[0];

			foreach (float possibleValue in possibleValues)
			{
                float closestDistance = Mathf.Abs(closestValue - value);
                float possibleDistance = Mathf.Abs(possibleValue - value);

                if (closestDistance > possibleDistance)
				{
					closestValue = possibleValue;
				}
                else if (closestDistance == possibleDistance)
                {                    
                    if ((pickSmallestDistance && closestValue > possibleValue) || (!pickSmallestDistance && closestValue < possibleValue))
                    {
                        closestValue = (value < 0) ? closestValue : possibleValue;
                    }
                }
			}
            return closestValue;

        }

        /// <summary>
        /// 根据参数中的角度返回 Vector3
        /// </summary>
        /// <param name="angle"></param>
        /// <returns></returns>
        public static Vector3 DirectionFromAngle(float angle, float additionalAngle)
        {
            angle += additionalAngle;

            Vector3 direction = Vector3.zero;
            direction.x = Mathf.Sin(angle * Mathf.Deg2Rad);
            direction.y = 0f;
            direction.z = Mathf.Cos(angle * Mathf.Deg2Rad);
            return direction;
        }

        /// <summary>
        /// 根据参数中的角度返回 Vector3
        /// </summary>
        /// <param name="angle"></param>
        /// <returns></returns>
        public static Vector3 DirectionFromAngle2D(float angle, float additionalAngle)
        {
            angle += additionalAngle;

            Vector3 direction = Vector3.zero;
            direction.x = Mathf.Cos(angle * Mathf.Deg2Rad);
            direction.y = Mathf.Sin(angle * Mathf.Deg2Rad);
            direction.z = 0f;
            return direction;
        }
        
        /// <summary>
        /// 获取[lower，upper)之间的随机数
        /// </summary>
        /// <param name="lower"></param>
        /// <param name="upper"></param>
        /// <returns></returns>
        public static int RandomNumber(int lower, int upper)
        {        
	        System.Random random = new System.Random();
	        int value = random.Next(lower, upper);
	        return value;
        }
	}
}