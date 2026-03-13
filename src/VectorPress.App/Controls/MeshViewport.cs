// Renders a generated triangle mesh with basic orbit, pan, and zoom controls.
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using VectorPress.Core.Models;

namespace VectorPress.App.Controls;

public sealed class MeshViewport : Control
{
	public static readonly StyledProperty<TriangleMesh?> MeshProperty =
		AvaloniaProperty.Register<MeshViewport, TriangleMesh?>(nameof(Mesh));

	private const float NearPlane = 0.1f;
	private const float AmbientLight = 0.35f;
	private const float DiffuseLight = 0.65f;

	private Point? _lastPointerPosition;
	private bool _isPanning;
	private float _yaw = -0.65f;
	private float _pitch = 0.5f;
	private float _distance = 200f;
	private Vector3 _targetOffset = Vector3.Zero;

	public TriangleMesh? Mesh
	{
		get => GetValue(MeshProperty);
		set => SetValue(MeshProperty, value);
	}

	public MeshViewport()
	{
		ClipToBounds = true;
	}

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);

		if (change.Property == MeshProperty)
		{
			ResetCamera();
			InvalidateVisual();
		}
	}

	public override void Render(DrawingContext context)
	{
		base.Render(context);
		context.FillRectangle(new SolidColorBrush(Color.Parse("#FFF8F7F2")), Bounds);

		var mesh = Mesh;
		if (mesh is null || !mesh.HasGeometry || Bounds.Width <= 0 || Bounds.Height <= 0)
		{
			return;
		}

		var projected = Project(mesh);
		if (projected.Count == 0)
		{
			return;
		}

		var brushes = new Dictionary<uint, SolidColorBrush>();
		var pen = new Pen(new SolidColorBrush(Color.Parse("#22000000")), 1);
		projected.Sort(static (left, right) => right.Depth.CompareTo(left.Depth));

		foreach (var triangle in projected)
		{
			var geometry = new StreamGeometry();
			using (var geometryContext = geometry.Open())
			{
				geometryContext.BeginFigure(triangle.A, true);
				geometryContext.LineTo(triangle.B);
				geometryContext.LineTo(triangle.C);
				geometryContext.EndFigure(true);
			}

			var shadedColor = Shade(triangle.Color, triangle.Light);
			var brushKey = ((uint)shadedColor.A << 24) | ((uint)shadedColor.R << 16) | ((uint)shadedColor.G << 8) | shadedColor.B;
			if (!brushes.TryGetValue(brushKey, out var brush))
			{
				brush = new SolidColorBrush(shadedColor);
				brushes.Add(brushKey, brush);
			}

			context.DrawGeometry(brush, pen, geometry);
		}
	}

	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);
		if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			_lastPointerPosition = e.GetPosition(this);
			_isPanning = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
			e.Pointer.Capture(this);
		}
	}

	protected override void OnPointerMoved(PointerEventArgs e)
	{
		base.OnPointerMoved(e);
		if (_lastPointerPosition is null || Mesh is null)
		{
			return;
		}

		var position = e.GetPosition(this);
		var delta = position - _lastPointerPosition.Value;
		_lastPointerPosition = position;

		if (_isPanning)
		{
			var scale = MathF.Max(Mesh.Bounds.Width, Mesh.Bounds.Height) / (float)Math.Max(1d, Bounds.Width);
			_targetOffset.X -= (float)delta.X * scale;
			_targetOffset.Y += (float)delta.Y * scale;
		}
		else
		{
			_yaw += (float)delta.X * 0.01f;
			_pitch = Math.Clamp(_pitch - ((float)delta.Y * 0.01f), -1.35f, 1.35f);
		}

		InvalidateVisual();
	}

	protected override void OnPointerReleased(PointerReleasedEventArgs e)
	{
		base.OnPointerReleased(e);
		_lastPointerPosition = null;
		_isPanning = false;
		e.Pointer.Capture(null);
	}

	protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
	{
		base.OnPointerWheelChanged(e);
		_distance = Math.Clamp(_distance * MathF.Exp((float)(-e.Delta.Y * 0.12d)), 1f, 10000f);
		InvalidateVisual();
	}

	private void ResetCamera()
	{
		var mesh = Mesh;
		if (mesh is null)
		{
			return;
		}

		_targetOffset = Vector3.Zero;
		var radius = MathF.Max(mesh.Bounds.Width, MathF.Max(mesh.Bounds.Height, mesh.Bounds.Depth));
		_distance = MathF.Max(radius * 2.5f, 20f);
	}

	private List<ProjectedTriangle> Project(TriangleMesh mesh)
	{
		var result = new List<ProjectedTriangle>(mesh.Triangles.Count);
		var target = new Vector3(
			mesh.Bounds.CenterX + _targetOffset.X,
			mesh.Bounds.CenterY + _targetOffset.Y,
			mesh.Bounds.CenterZ + _targetOffset.Z);

		var cameraOffset = new Vector3(
			MathF.Cos(_yaw) * MathF.Cos(_pitch) * _distance,
			MathF.Sin(_yaw) * MathF.Cos(_pitch) * _distance,
			MathF.Sin(_pitch) * _distance);
		var camera = target + cameraOffset;
		var forward = Vector3.Normalize(target - camera);
		var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
		if (right.LengthSquared() < 0.001f)
		{
			right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
		}

		var up = Vector3.Normalize(Vector3.Cross(right, forward));
		var lightDirection = Vector3.Normalize(new Vector3(-0.4f, -0.7f, 1f));
		var focalLength = (float)(Math.Min(Bounds.Width, Bounds.Height) * 0.85d);
		var center = new Point(Bounds.Width * 0.5d, Bounds.Height * 0.5d);

		foreach (var triangle in mesh.Triangles)
		{
			var a = new Vector3(triangle.Ax, triangle.Ay, triangle.Az);
			var b = new Vector3(triangle.Bx, triangle.By, triangle.Bz);
			var c = new Vector3(triangle.Cx, triangle.Cy, triangle.Cz);

			var cameraA = ToCameraSpace(a, camera, right, up, forward);
			var cameraB = ToCameraSpace(b, camera, right, up, forward);
			var cameraC = ToCameraSpace(c, camera, right, up, forward);
			if (cameraA.Z <= NearPlane || cameraB.Z <= NearPlane || cameraC.Z <= NearPlane)
			{
				continue;
			}

			var pointA = ProjectPoint(cameraA, center, focalLength);
			var pointB = ProjectPoint(cameraB, center, focalLength);
			var pointC = ProjectPoint(cameraC, center, focalLength);
			var screenArea = ((pointB.X - pointA.X) * (pointC.Y - pointA.Y)) - ((pointB.Y - pointA.Y) * (pointC.X - pointA.X));
			if (screenArea >= 0d)
			{
				continue;
			}

			var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
			if (float.IsNaN(normal.X))
			{
				continue;
			}

			var light = Math.Clamp(AmbientLight + (MathF.Max(0f, Vector3.Dot(normal, lightDirection)) * DiffuseLight), 0f, 1f);
			result.Add(new ProjectedTriangle(pointA, pointB, pointC, (cameraA.Z + cameraB.Z + cameraC.Z) / 3f, triangle.Color, light));
		}

		return result;
	}

	private static Vector3 ToCameraSpace(Vector3 point, Vector3 camera, Vector3 right, Vector3 up, Vector3 forward)
	{
		var relative = point - camera;
		return new Vector3(
			Vector3.Dot(relative, right),
			Vector3.Dot(relative, up),
			Vector3.Dot(relative, forward));
	}

	private static Point ProjectPoint(Vector3 point, Point center, float focalLength)
	{
		var scale = focalLength / point.Z;
		return new Point(center.X + (point.X * scale), center.Y - (point.Y * scale));
	}

	private static Color Shade(RgbaColor color, float intensity)
	{
		static byte Scale(byte channel, float amount) => (byte)Math.Clamp((int)MathF.Round(channel * amount), 0, 255);
		return Color.FromArgb(color.A, Scale(color.R, intensity), Scale(color.G, intensity), Scale(color.B, intensity));
	}

	private sealed record ProjectedTriangle(
		Point A,
		Point B,
		Point C,
		float Depth,
		RgbaColor Color,
		float Light);
}
