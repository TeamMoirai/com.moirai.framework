using UnityEngine;
using Debug = UnityEngine.Debug;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Moirai.Atropos
{	
	/// <summary>
	/// Debug Draw helpers
	/// </summary>
	public static class DebugDrawHelper
	{
        #region EnableDisableDebugs

#if UNITY_EDITOR
        private static bool s_SettingCached = false;
#endif
        private static bool s_DebugDrawEnabled = false;
        private const string DEBUG_DRAWS_KEY = "DebugDrawsEnabled";
        /// <summary>
        /// 是否应执行调试绘制
        /// </summary>
        public static bool DebugDrawEnabled
        {
            get
            {
#if UNITY_EDITOR
	            if (!s_SettingCached)
	            {
		            s_DebugDrawEnabled = EditorPrefs.GetBool(DEBUG_DRAWS_KEY, true);
		            s_SettingCached = true;
	            }
#endif
	            return s_DebugDrawEnabled;
            }
	        set
            {
#if UNITY_EDITOR
	            EditorPrefs.SetBool(DEBUG_DRAWS_KEY, value);
	            s_SettingCached = true;
#endif
	            s_DebugDrawEnabled = value;
            }
        }

        #endregion

        #region Casts

        /// <summary>
        /// 投射常规 2D 射线并绘制调试射线
        /// </summary>
        /// <returns>射线检测到的对象</returns>
        /// <param name="rayOriginPoint">射线原点</param>
        /// <param name="rayDirection">射线方向</param>
        /// <param name="rayDistance">射线距离</param>
        /// <param name="mask">遮罩</param>
        /// <param name="color">颜色</param>
        /// <param name="drawGizmo">如果为<c>true</c>则绘制调试射线</param>
        public static RaycastHit2D RayCast(Vector2 rayOriginPoint, Vector2 rayDirection, float rayDistance, LayerMask mask, Color color,bool drawGizmo=false)
		{	
			if (drawGizmo && DebugDrawEnabled) 
			{
				Debug.DrawRay(rayOriginPoint, rayDirection * rayDistance, color);
			}
			return Physics2D.Raycast(rayOriginPoint, rayDirection, rayDistance, mask);		
		}
        
        /// <summary>
        /// 投射常规 2D 射线并绘制调试射线
        /// </summary>
        /// <returns>射线检测到的对象</returns>
        /// <param name="rayOriginPoint">射线原点</param>
        /// <param name="rayDirection">射线方向</param>
        /// <param name="rayDistance">射线距离</param>
        /// <param name="mask">遮罩</param>
        /// <param name="color">颜色</param>
        /// <param name="drawGizmo">如果为<c>true</c>则绘制调试射线</param>
        public static RaycastHit2D[] RayCastAll(Vector2 rayOriginPoint, Vector2 rayDirection, float rayDistance, LayerMask mask, Color color,bool drawGizmo=false)
		{	
			if (drawGizmo && DebugDrawEnabled) 
			{
				Debug.DrawRay(rayOriginPoint, rayDirection * rayDistance, color);
			}
			return Physics2D.RaycastAll(rayOriginPoint, rayDirection,rayDistance, mask);		
		}

        /// <summary>
        /// 投射 2D 箱型射线并绘制调试射线
        /// </summary>
        /// <param name="origin">射线原点</param>
        /// <param name="size">箱型射线大小</param>
        /// <param name="angle">射线角度（以度为单位）</param>
        /// <param name="direction">射线方向</param>
        /// <param name="length">射线距离</param>
        /// <param name="mask">遮罩</param>
        /// <param name="color">颜色</param>
        /// <param name="drawGizmo">如果为<c>true</c>则绘制调试射线</param>
        /// <returns></returns>
        public static RaycastHit2D BoxCast(Vector2 origin, Vector2 size, float angle, Vector2 direction, float length, LayerMask mask, Color color, bool drawGizmo = false)
        {
            if (drawGizmo && DebugDrawEnabled)
            {
                Quaternion rotation = Quaternion.Euler(0f, 0f, angle);

                Vector3[] points = new Vector3[8];

                float halfSizeX = size.x / 2f;
                float halfSizeY = size.y / 2f;

                points[0] = rotation * (origin + (Vector2.left * halfSizeX) + (Vector2.up * halfSizeY));  // 左上
                points[1] = rotation * (origin + (Vector2.right * halfSizeX) + (Vector2.up * halfSizeY)); // 右上
                points[2] = rotation * (origin + (Vector2.right * halfSizeX) - (Vector2.up * halfSizeY)); // 右下
                points[3] = rotation * (origin + (Vector2.left * halfSizeX) - (Vector2.up * halfSizeY));  // 左下
                
                points[4] = rotation * ((origin + Vector2.left * halfSizeX + Vector2.up * halfSizeY) + length * direction);  // 左上
                points[5] = rotation * ((origin + Vector2.right * halfSizeX + Vector2.up * halfSizeY) + length * direction); // 右上
                points[6] = rotation * ((origin + Vector2.right * halfSizeX - Vector2.up * halfSizeY) + length * direction); // 右下
                points[7] = rotation * ((origin + Vector2.left * halfSizeX - Vector2.up * halfSizeY) + length * direction);  // 左下
                                
                Debug.DrawLine(points[0], points[1], color);
                Debug.DrawLine(points[1], points[2], color);
                Debug.DrawLine(points[2], points[3], color);
                Debug.DrawLine(points[3], points[0], color);

                Debug.DrawLine(points[4], points[5], color);
                Debug.DrawLine(points[5], points[6], color);
                Debug.DrawLine(points[6], points[7], color);
                Debug.DrawLine(points[7], points[4], color);
                
                Debug.DrawLine(points[0], points[4], color);
                Debug.DrawLine(points[1], points[5], color);
                Debug.DrawLine(points[2], points[6], color);
                Debug.DrawLine(points[3], points[7], color);

            }
            return Physics2D.BoxCast(origin, size, angle, direction, length, mask);
        }

        /// <summary>
        /// 在不分配内存的情况下绘制调试射线
        /// </summary>
        /// <returns>在不分配内存的射线</returns>
        /// <param name="array">数组</param>
        /// <param name="rayOriginPoint">射线原点</param>
        /// <param name="rayDirection">射线方向</param>
        /// <param name="rayDistance">射线距离</param>
        /// <param name="mask">遮罩</param>
        /// <param name="color">颜色</param>
        /// <param name="drawGizmo">如果为<c>true</c>则绘制调试射线</param>
        public static RaycastHit2D MonoRayCastNonAlloc(RaycastHit2D[] array, Vector2 rayOriginPoint, Vector2 rayDirection, float rayDistance, LayerMask mask, Color color, bool drawGizmo=false)
		{	
			if (drawGizmo && DebugDrawEnabled) 
			{
				Debug.DrawRay (rayOriginPoint, rayDirection * rayDistance, color);
			}
			if (Physics2D.RaycastNonAlloc(rayOriginPoint, rayDirection, array, rayDistance, mask) > 0)
			{
				return array[0];
			}
			return new RaycastHit2D();	
		}

		/// <summary>
		/// 投射常规 3D 射线并绘制调试射线
		/// </summary>
		/// <returns>射线检测到的对象</returns>
		/// <param name="rayOriginPoint">射线原点</param>
		/// <param name="rayDirection">射线方向</param>
		/// <param name="rayDistance">射线距离</param>
		/// <param name="mask">遮罩</param>
		/// <param name="color">颜色</param>
		/// <param name="drawGizmo">如果为<c>true</c>则绘制调试射线</param>
		/// <param name="queryTriggerInteraction">指定此查询是否应命中触发器</param>
		public static RaycastHit Raycast3D(Vector3 rayOriginPoint, Vector3 rayDirection, float rayDistance, LayerMask mask, Color color, bool drawGizmo=false, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
		{
			if (drawGizmo && DebugDrawEnabled) 
			{
				Debug.DrawRay(rayOriginPoint, rayDirection * rayDistance, color);
			}
			RaycastHit hit;
			Physics.Raycast(rayOriginPoint, rayDirection, out hit, rayDistance, mask, queryTriggerInteraction);	
			return hit;
		}

        #endregion

        #region DebugDraw

        /// <summary>
        /// 绘制从原点位置沿 Vector3 方向的调试箭头
        /// </summary>
        /// <param name="origin">原点</param>
        /// <param name="direction">方向</param>
        /// <param name="color">颜色</param>
        /// <param name="arrowHeadLength">箭头长度</param>
        /// <param name="arrowHeadAngle">箭头角度</param>
        public static void DrawGizmoArrow(Vector3 origin, Vector3 direction, Color color, float arrowHeadLength = 3f, float arrowHeadAngle = 25f)
	    {
            if (!DebugDrawEnabled)
            {
                return;
            }

	        Gizmos.color = color;
	        Gizmos.DrawRay(origin, direction);
	       
			DrawArrowEnd(true, origin, direction, color, arrowHeadLength, arrowHeadAngle);
	    }

	    /// <summary>
	    /// 绘制一个从原点位置沿 Vector3 方向的调试箭头
	    /// </summary>
	    /// <param name="origin">原点</param>
	    /// <param name="direction">方向</param>
	    /// <param name="color">颜色</param>
	    /// <param name="arrowHeadLength">箭头长度</param>
	    /// <param name="arrowHeadAngle">箭头角度</param>
	    public static void DebugDrawArrow(Vector3 origin, Vector3 direction, Color color, float arrowHeadLength = 0.2f, float arrowHeadAngle = 35f)
        {
            if (!DebugDrawEnabled)
            {
                return;
            }

            Debug.DrawRay(origin, direction, color);
	       
			DrawArrowEnd(false, origin, direction,color, arrowHeadLength, arrowHeadAngle);
	    }

		/// <summary>
		/// 绘制一个从原点位置沿 Vector3 方向的调试箭头
		/// </summary>
		/// <param name="origin">原点</param>
		/// <param name="direction">方向</param>
		/// <param name="color">颜色</param>
		/// <param name="arrowLength">箭头长度</param>
		/// <param name="arrowHeadLength">箭头长度</param>
		/// <param name="arrowHeadAngle">箭头角度</param>
		public static void DebugDrawArrow(Vector3 origin, Vector3 direction, Color color, float arrowLength, float arrowHeadLength = 0.20f, float arrowHeadAngle = 35.0f)
        {
            if (!DebugDrawEnabled)
            {
                return;
            }

            Debug.DrawRay(origin, direction * arrowLength, color);

			DrawArrowEnd(false, origin, direction * arrowLength, color, arrowHeadLength, arrowHeadAngle);
		}

		/// <summary>
		/// 在指定点绘制指定大小和颜色的调试十字
		/// </summary>
		/// <param name="spot">点</param>
		/// <param name="crossSize">十字大小</param>
		/// <param name="color">颜色</param>
		public static void DebugDrawCross(Vector3 spot, float crossSize, Color color)
        {
            if (!DebugDrawEnabled)
            {
                return;
            }

            Vector3 tempOrigin = Vector3.zero;
			Vector3 tempDirection = Vector3.zero;

			tempOrigin.x = spot.x - crossSize / 2;
			tempOrigin.y = spot.y - crossSize / 2;
            tempOrigin.z = spot.z ;
            tempDirection.x = 1; 
			tempDirection.y = 1;
            tempDirection.z = 0;
            Debug.DrawRay(tempOrigin, tempDirection * crossSize, color);

			tempOrigin.x = spot.x - crossSize / 2;
            tempOrigin.y = spot.y + crossSize / 2;
            tempOrigin.z = spot.z ;
            tempDirection.x = 1; 
			tempDirection.y = -1;
            tempDirection.z = 0;
            Debug.DrawRay(tempOrigin, tempDirection * crossSize, color);
		}

		/// <summary>
		/// 绘制 DebugDrawArrow 的箭头末端
		/// </summary>
		/// <param name="drawGizmos">如果为<c>true</c>则绘制调试</param>
		/// <param name="arrowEndPosition">箭头结束位置</param>
		/// <param name="direction">方向</param>
		/// <param name="color">颜色</param>
		/// <param name="arrowHeadLength">箭头长度</param>
		/// <param name="arrowHeadAngle">箭头角度</param>
		private static void DrawArrowEnd(bool drawGizmos, Vector3 arrowEndPosition, Vector3 direction, Color color, float arrowHeadLength = 0.25f, float arrowHeadAngle = 40.0f)
        {
            if (!DebugDrawEnabled)
            {
                return;
            }

            if (direction == Vector3.zero)
			{
				return;
			}
	        Vector3 right = Quaternion.LookRotation(direction) * Quaternion.Euler(arrowHeadAngle, 0, 0) * Vector3.back;
	        Vector3 left = Quaternion.LookRotation(direction) * Quaternion.Euler(-arrowHeadAngle, 0, 0) * Vector3.back;
	        Vector3 up = Quaternion.LookRotation(direction) * Quaternion.Euler(0, arrowHeadAngle, 0) * Vector3.back;
	        Vector3 down = Quaternion.LookRotation(direction) * Quaternion.Euler(0, -arrowHeadAngle, 0) * Vector3.back;
	        if (drawGizmos) 
	        {
	            Gizmos.color = color;
	            Gizmos.DrawRay(arrowEndPosition + direction, right * arrowHeadLength);
	            Gizmos.DrawRay(arrowEndPosition + direction, left * arrowHeadLength);
	            Gizmos.DrawRay(arrowEndPosition + direction, up * arrowHeadLength);
	            Gizmos.DrawRay(arrowEndPosition + direction, down * arrowHeadLength);
	        }
	        else
	        {
	            Debug.DrawRay(arrowEndPosition + direction, right * arrowHeadLength, color);
	            Debug.DrawRay(arrowEndPosition + direction, left * arrowHeadLength, color);
	            Debug.DrawRay(arrowEndPosition + direction, up * arrowHeadLength, color);
	            Debug.DrawRay(arrowEndPosition + direction, down * arrowHeadLength, color);
	        }
	    }

		/// <summary>
		/// 绘制调试以在屏幕上具体化对象的边界。
		/// </summary>
		/// <param name="bounds">边界</param>
		/// <param name="color">颜色</param>
		public static void DrawHandlesBounds(Bounds bounds, Color color)
        {
            if (!DebugDrawEnabled)
            {
                return;
            }

#if UNITY_EDITOR
            Vector3 boundsCenter = bounds.center;
		    Vector3 boundsExtents = bounds.extents;
		  
			Vector3 v3FrontTopLeft     = new Vector3(boundsCenter.x - boundsExtents.x, boundsCenter.y + boundsExtents.y, boundsCenter.z - boundsExtents.z);  // Front top left corner
			Vector3 v3FrontTopRight    = new Vector3(boundsCenter.x + boundsExtents.x, boundsCenter.y + boundsExtents.y, boundsCenter.z - boundsExtents.z);  // Front top right corner
			Vector3 v3FrontBottomLeft  = new Vector3(boundsCenter.x - boundsExtents.x, boundsCenter.y - boundsExtents.y, boundsCenter.z - boundsExtents.z);  // Front bottom left corner
			Vector3 v3FrontBottomRight = new Vector3(boundsCenter.x + boundsExtents.x, boundsCenter.y - boundsExtents.y, boundsCenter.z - boundsExtents.z);  // Front bottom right corner
			Vector3 v3BackTopLeft      = new Vector3(boundsCenter.x - boundsExtents.x, boundsCenter.y + boundsExtents.y, boundsCenter.z + boundsExtents.z);  // Back top left corner
			Vector3 v3BackTopRight     = new Vector3(boundsCenter.x + boundsExtents.x, boundsCenter.y + boundsExtents.y, boundsCenter.z + boundsExtents.z);  // Back top right corner
			Vector3 v3BackBottomLeft   = new Vector3(boundsCenter.x - boundsExtents.x, boundsCenter.y - boundsExtents.y, boundsCenter.z + boundsExtents.z);  // Back bottom left corner
			Vector3 v3BackBottomRight  = new Vector3(boundsCenter.x + boundsExtents.x, boundsCenter.y - boundsExtents.y, boundsCenter.z + boundsExtents.z);  // Back bottom right corner


			Handles.color = color;
			
			Handles.DrawLine(v3FrontTopLeft, v3FrontTopRight);
			Handles.DrawLine(v3FrontTopRight, v3FrontBottomRight);
			Handles.DrawLine(v3FrontBottomRight, v3FrontBottomLeft);
			Handles.DrawLine(v3FrontBottomLeft, v3FrontTopLeft);
			
			Handles.DrawLine(v3BackTopLeft, v3BackTopRight);
			Handles.DrawLine(v3BackTopRight, v3BackBottomRight);
			Handles.DrawLine(v3BackBottomRight, v3BackBottomLeft);
			Handles.DrawLine(v3BackBottomLeft, v3BackTopLeft);
			
			Handles.DrawLine(v3FrontTopLeft, v3BackTopLeft);
			Handles.DrawLine(v3FrontTopRight, v3BackTopRight);
			Handles.DrawLine(v3FrontBottomRight, v3BackBottomRight);
			Handles.DrawLine(v3FrontBottomLeft, v3BackBottomLeft);
#endif
		}

        /// <summary>
        /// 在指定位置和大小以及指定颜色处绘制一个实心矩形
        /// </summary>
        /// <param name="position"></param>
        /// <param name="size"></param>
        /// <param name="borderColor"></param>
        /// <param name="solidColor"></param>
        public static void DrawSolidRectangle(Vector3 position, Vector3 size, Color borderColor, Color solidColor)
        {
            if (!DebugDrawEnabled)
            {
                return;
            }

#if UNITY_EDITOR

            Vector3 halfSize = size / 2f;

            Vector3[] verts = new Vector3[4];
            verts[0] = new Vector3(halfSize.x, halfSize.y, halfSize.z);
            verts[1] = new Vector3(-halfSize.x, halfSize.y, halfSize.z);
            verts[2] = new Vector3(-halfSize.x, -halfSize.y, halfSize.z);
            verts[3] = new Vector3(halfSize.x, -halfSize.y, halfSize.z);
            Handles.DrawSolidRectangleWithOutline(verts, solidColor, borderColor);
            
#endif
        }
        
        /// <summary>
        /// 在指定位置绘制指定大小和颜色的球体
        /// </summary>
        /// <param name="position">位置</param>
        /// <param name="size">大小</param>
        /// <param name="color">颜色</param>
        public static void DrawGizmoPoint(Vector3 position, float size, Color color)
        {
            if (!DebugDrawEnabled)
            {
                return;
            }
            Gizmos.color = color;
			Gizmos.DrawWireSphere(position, size);
		}

		/// <summary>
		/// 在指定位置绘制指定颜色和大小的立方体
		/// </summary>
		/// <param name="position">位置</param>
		/// <param name="color">颜色</param>
		/// <param name="size">大小</param>
		public static void DrawCube(Vector3 position, Color color, Vector3 size)
        {
            if (!DebugDrawEnabled)
            {
                return;
            }

            Vector3 halfSize = size / 2f; 

			Vector3[] points = new Vector3 []
			{
				position + new Vector3(halfSize.x,halfSize.y,halfSize.z),
				position + new Vector3(-halfSize.x,halfSize.y,halfSize.z),
				position + new Vector3(-halfSize.x,-halfSize.y,halfSize.z),
				position + new Vector3(halfSize.x,-halfSize.y,halfSize.z),			
				position + new Vector3(halfSize.x,halfSize.y,-halfSize.z),
				position + new Vector3(-halfSize.x,halfSize.y,-halfSize.z),
				position + new Vector3(-halfSize.x,-halfSize.y,-halfSize.z),
				position + new Vector3(halfSize.x,-halfSize.y,-halfSize.z),
			};

			Debug.DrawLine (points[0], points[1], color ); 
			Debug.DrawLine (points[1], points[2], color ); 
			Debug.DrawLine (points[2], points[3], color ); 
			Debug.DrawLine (points[3], points[0], color ); 
		}

        /// <summary>
        /// 在指定位置、偏移和指定大小处绘制立方体
        /// </summary>
        /// <param name="transform"></param>
        /// <param name="offset"></param>
        /// <param name="cubeSize"></param>
        /// <param name="wireOnly"></param>
        public static void DrawGizmoCube(Transform transform, Vector3 offset, Vector3 cubeSize, bool wireOnly)
        {
            if (!DebugDrawEnabled)
            {
                return;
            }

            Matrix4x4 rotationMatrix = transform.localToWorldMatrix;
            Gizmos.matrix = rotationMatrix;
            if (wireOnly)
            {
                Gizmos.DrawWireCube(offset, cubeSize);
            }
            else
            {
                Gizmos.DrawCube(offset, cubeSize);
            }
        }

		/// <summary>
		/// 绘制矩形
		/// </summary>
		/// <param name="center">中心</param>
		/// <param name="size">大小</param>
		/// <param name="color">颜色</param>
		public static void DrawGizmoRectangle(Vector2 center, Vector2 size, Color color)
        {
            if (!DebugDrawEnabled)
            {
                return;
            }

            Gizmos.color = color;

			Vector3 v3TopLeft = new Vector3(center.x - size.x/2, center.y + size.y/2, 0);
			Vector3 v3TopRight = new Vector3(center.x + size.x/2, center.y + size.y/2, 0);;
			Vector3 v3BottomRight = new Vector3(center.x + size.x/2, center.y - size.y/2, 0);;
			Vector3 v3BottomLeft = new Vector3(center.x - size.x/2, center.y - size.y/2, 0);;

			Gizmos.DrawLine(v3TopLeft,v3TopRight);
			Gizmos.DrawLine(v3TopRight,v3BottomRight);
			Gizmos.DrawLine(v3BottomRight,v3BottomLeft);
			Gizmos.DrawLine(v3BottomLeft,v3TopLeft);
        }

		/// <summary>
		/// 绘制矩形
		/// </summary>
		/// <param name="center">中心</param>
		/// <param name="size">大小</param>
		/// <param name="rotationMatrix">旋转矩阵</param>
		/// <param name="color">颜色</param>
		public static void DrawGizmoRectangle(Vector2 center, Vector2 size, Matrix4x4 rotationMatrix, Color color)
        {
            if (!DebugDrawEnabled)
            {
                return;
            }

            GL.PushMatrix();

            Gizmos.color = color;

            Vector3 v3TopLeft = rotationMatrix * new Vector3(center.x - size.x / 2, center.y + size.y / 2, 0);
            Vector3 v3TopRight = rotationMatrix * new Vector3(center.x + size.x / 2, center.y + size.y / 2, 0); ;
            Vector3 v3BottomRight = rotationMatrix * new Vector3(center.x + size.x / 2, center.y - size.y / 2, 0); ;
            Vector3 v3BottomLeft = rotationMatrix * new Vector3(center.x - size.x / 2, center.y - size.y / 2, 0); ;

            
            Gizmos.DrawLine(v3TopLeft, v3TopRight);
            Gizmos.DrawLine(v3TopRight, v3BottomRight);
            Gizmos.DrawLine(v3BottomRight, v3BottomLeft);
            Gizmos.DrawLine(v3BottomLeft, v3TopLeft);
            GL.PopMatrix();
        }

        /// <summary>
        /// 基于 Rect 和颜色绘制矩形
        /// </summary>
        /// <param name="rectangle">Rect 矩形</param>
        /// <param name="color">颜色</param>
        public static void DrawRectangle(Rect rectangle, Color color)
        {
            if (!DebugDrawEnabled)
            {
                return;
            }

            Vector3 pos = new Vector3( rectangle.x + rectangle.width/2, rectangle.y + rectangle.height/2, 0.0f );
			Vector3 scale = new Vector3 (rectangle.width, rectangle.height, 0.0f );

			DebugDrawHelper.DrawRectangle(pos, color, scale); 
		}	

		/// <summary>
		/// 在指定位置绘制指定颜色和大小的矩形
		/// </summary>
		/// <param name="position">位置</param>
		/// <param name="color">颜色</param>
		/// <param name="size">大小</param>
		public static void DrawRectangle(Vector3 position, Color color, Vector3 size)
        {
            if (!DebugDrawEnabled)
            {
                return;
            }

            Vector3 halfSize = size / 2f; 

			Vector3[] points = new Vector3 []
			{
				position + new Vector3(halfSize.x,halfSize.y,halfSize.z),
				position + new Vector3(-halfSize.x,halfSize.y,halfSize.z),
				position + new Vector3(-halfSize.x,-halfSize.y,halfSize.z),
				position + new Vector3(halfSize.x,-halfSize.y,halfSize.z),	
			};

			Debug.DrawLine(points[0], points[1], color ); 
			Debug.DrawLine(points[1], points[2], color ); 
			Debug.DrawLine(points[2], points[3], color ); 
			Debug.DrawLine(points[3], points[0], color ); 
		}

		/// <summary>
		/// 在指定位置绘制指定颜色和大小的点
		/// </summary>
		/// <param name="position">位置</param>
		/// <param name="color">颜色</param>
		/// <param name="size">大小</param>
		public static void DrawPoint(Vector3 position, Color color, float size)
        {
            if (!DebugDrawEnabled)
            {
                return;
            }

            Vector3[] points = new Vector3[] 
			{
				position + (Vector3.up * size), 
				position - (Vector3.up * size), 
				position + (Vector3.right * size), 
				position - (Vector3.right * size), 
				position + (Vector3.forward * size), 
				position - (Vector3.forward * size)
			}; 		

			Debug.DrawLine(points[0], points[1], color ); 
			Debug.DrawLine(points[2], points[3], color ); 
			Debug.DrawLine(points[4], points[5], color ); 
			Debug.DrawLine(points[0], points[2], color ); 
			Debug.DrawLine(points[0], points[3], color ); 
			Debug.DrawLine(points[0], points[4], color ); 
			Debug.DrawLine(points[0], points[5], color ); 
			Debug.DrawLine(points[1], points[2], color ); 
			Debug.DrawLine(points[1], points[3], color ); 
			Debug.DrawLine(points[1], points[4], color ); 
			Debug.DrawLine(points[1], points[5], color ); 
			Debug.DrawLine(points[4], points[2], color ); 
			Debug.DrawLine(points[4], points[3], color ); 
			Debug.DrawLine(points[5], points[2], color ); 
			Debug.DrawLine(points[5], points[3], color ); 
		}

        /// <summary>
        /// 绘制指定颜色和大小的线
        /// </summary>
        /// <param name="position">位置</param>
        /// <param name="color">颜色</param>
        /// <param name="size">大小</param>
        public static void DrawGizmoPoint(Vector3 position, Color color, float size)
        {
	        if (!DebugDrawEnabled)
	        {
		        return;
	        }

	        Vector3[] points = new Vector3[] 
	        {
		        position + (Vector3.up * size), 
		        position - (Vector3.up * size), 
		        position + (Vector3.right * size), 
		        position - (Vector3.right * size), 
		        position + (Vector3.forward * size), 
		        position - (Vector3.forward * size)
	        }; 		

	        Gizmos.color = color;
	        Gizmos.DrawLine(points[0], points[1]); 
	        Gizmos.DrawLine(points[2], points[3]); 
	        Gizmos.DrawLine(points[4], points[5]); 
	        Gizmos.DrawLine(points[0], points[2]); 
	        Gizmos.DrawLine(points[0], points[3]); 
	        Gizmos.DrawLine(points[0], points[4]); 
	        Gizmos.DrawLine(points[0], points[5]); 
	        Gizmos.DrawLine(points[1], points[2]); 
	        Gizmos.DrawLine(points[1], points[3]); 
	        Gizmos.DrawLine(points[1], points[4]); 
	        Gizmos.DrawLine(points[1], points[5]); 
	        Gizmos.DrawLine(points[4], points[2]); 
	        Gizmos.DrawLine(points[4], points[3]); 
	        Gizmos.DrawLine(points[5], points[2]); 
	        Gizmos.DrawLine(points[5], points[3]); 
        }
        
        #endregion
    }
}