# if MATHEMATICS_INSTALLED
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// Mathematics 数学工具集。
    /// </summary>
    /// <remarks>方法应标注 <see cref="MethodImplOptions.AggressiveInlining"/>，
    /// 使 Burst 编译器在优化方法调用方的同时一并优化该方法本身。</remarks>
    public static partial class MathsUtility
    {
        /// <summary>
        /// <see cref="float4x4"/> 版的 <see cref="Matrix4x4.MultiplyVector(in Vector3)"/>。
        /// </summary>
        /// <param name="worldMatrix">世界变换矩阵。</param>
        /// <param name="point">待变换的方向向量。</param>
        /// <param name="result">变换结果。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MultiplyVector(ref this float4x4 worldMatrix, in float3 point, ref float3 result)
        {
            result = math.mul(worldMatrix, new float4(point, 0.0f)).xyz;
        }
        
        // ReSharper disable once InconsistentNaming
        /// <summary>
        /// <see cref="float4x4"/> 版的 <see cref="Matrix4x4.MultiplyPoint3x4(in Vector3)"/>。
        /// </summary>
        /// <param name="worldMatrix">世界变换矩阵。</param>
        /// <param name="point">待变换的点。</param>
        /// <param name="result">变换结果。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MultiplyPoint3x4(ref this float4x4 worldMatrix, in float3 point, ref float3 result)
        {
            result = math.mul(worldMatrix, new float4(point, 1.0f)).xyz;
        }
        
        // 感谢 https://discussions.unity.com/t/rotate-towards-c-jobs/778453/5 提供的思路
        /// <summary>
        /// 将四元数 <paramref name="from"/> 向 <paramref name="to"/> 旋转，旋转角度不超过 <paramref name="maxDegreesDelta"/> 度。
        /// </summary>
        /// <param name="from">起始旋转。</param>
        /// <param name="to">目标旋转。</param>
        /// <param name="maxDegreesDelta">本次调用的最大旋转角度（度）。</param>
        /// <returns>旋转后的四元数，已到达目标时直接返回 <paramref name="to"/>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static quaternion RotateTowards(in quaternion from, in quaternion to, in float maxDegreesDelta)
        {
            float num = Angle(from, to);
            return num < float.Epsilon ? to : math.slerp(from, to, math.min(1f, maxDegreesDelta / num));
        }

        /// <summary>
        /// 计算两个四元数旋转之间的夹角（度）。
        /// </summary>
        /// <param name="q1">第一个四元数。</param>
        /// <param name="q2">第二个四元数。</param>
        /// <returns>两旋转之间的角度（度），相同或相反时返回 0。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Angle(in quaternion q1, in quaternion q2)
        {
            var dot = math.dot(q1, q2);
            return !(dot > 0.999998986721039) ? (float)(math.acos(math.min(math.abs(dot), 1f)) * 2.0) : 0.0f;
        }
        
        /// <summary>
        /// 判断目标位置是否位于观察者的水平视野范围内（忽略高度差，仅取水平方向计算夹角）。
        /// </summary>
        /// <param name="center">观察者位置。</param>
        /// <param name="position">目标位置。</param>
        /// <param name="forward">观察者朝向。</param>
        /// <param name="angle">总视野角度（度）。</param>
        /// <returns>在视野内返回 <c>true</c>，否则返回 <c>false</c>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool InViewAngle(in float3 center, in float3 position, in float3 forward, in float angle)
        {
            float3 targetPosition = position;
            targetPosition.y = center.y;

            float3 directionToTarget = math.normalize(targetPosition - center);

            return Angle(forward, directionToTarget) <= angle / 2;
        }
        
        /// <summary>
        /// 计算两个向量之间的无符号夹角（度）。
        /// </summary>
        /// <param name="from">起始向量。</param>
        /// <param name="to">目标向量。</param>
        /// <returns>两向量之间的角度（度），任一向量为零向量时返回 0。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Angle(in float3 from, in float3 to)
        {
            float num = math.sqrt(math.sqrt(math.dot(from, from)) * math.sqrt(math.dot(to, to)));
            if (num < 1E-15f)
            {
                return 0f;
            }
            float num2 = math.clamp(math.dot(from, to) / num, -1f, 1f);
            return (float)math.acos(num2) * 57.29578f;
        }
        
        /// <summary>
        /// 判断指定层是否包含在层掩码中。
        /// </summary>
        /// <param name="layer">层索引。</param>
        /// <param name="mask">层掩码。</param>
        /// <returns>包含返回 <c>true</c>，否则返回 <c>false</c>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInLayerMask(in int layer, in LayerMask mask)
        {
            return (mask.value & (1 << layer)) != 0;
        }
        
        /// <summary>
        /// 判断点是否位于多边形内部（射线法，仅比较 x/z 分量）。
        /// </summary>
        /// <param name="polygonCorners">按顺序排列的多边形顶点集合。</param>
        /// <param name="p">待判断的点。</param>
        /// <returns>在多边形内部返回 <c>true</c>，否则返回 <c>false</c>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPointInPolygon(in NativeArray<float3> polygonCorners, in float3 p)
        {
            var j = polygonCorners.Length - 1;
            var inside = false;
            for (int i = 0; i < polygonCorners.Length; j = i++)
            {
                var pi = polygonCorners[i];
                var pj = polygonCorners[j];
                if (((pi.z <= p.z && p.z < pj.z) || (pj.z <= p.z && p.z < pi.z)) &&
                    (p.x < (pj.x - pi.x) * (p.z - pi.z) / (pj.z - pi.z) + pi.x))
                    inside = !inside;
            }
            return inside;
        }
        
        /// <summary>
        /// 二次贝塞尔曲线：基于三个点动态绘制曲线。
        /// </summary>
        /// <param name="t">插值系数，0 表示起点，1 表示终点。</param>
        /// <param name="p0">起始点。</param>
        /// <param name="p1">中间点。</param>
        /// <param name="p2">结束点。</param>
        /// <returns>曲线上的插值点。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 GetQuadraticCurvePoint(in float t, in float3 p0, in float3 p1, in float3 p2)
        {
            float u = 1 - t;
            float tt = t * t;
            float uu = u * u;
            return uu * p0 + 2 * u * t * p1 + tt * p2;
        }
    }
}
#endif