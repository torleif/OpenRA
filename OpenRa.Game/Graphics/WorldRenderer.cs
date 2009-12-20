using System.Drawing;
using System;
using System.Linq;
using System.Windows.Forms;
using IjwFramework.Types;
using System.Collections.Generic;
using OpenRa.Game.Traits;
using OpenRa.Game.Support;
using OpenRa.Game.Effects;

namespace OpenRa.Game.Graphics
{
	class WorldRenderer
	{
		public readonly TerrainRenderer terrainRenderer;
		public readonly SpriteRenderer spriteRenderer;
		public readonly LineRenderer lineRenderer;
		//public readonly Region region;
		public readonly UiOverlay uiOverlay;
		readonly Renderer renderer;

		public static bool ShowUnitPaths = false;

		public WorldRenderer(Renderer renderer)
		{
			terrainRenderer = new TerrainRenderer(renderer, Rules.Map);

			this.renderer = renderer;
			spriteRenderer = new SpriteRenderer(renderer, true);
			lineRenderer = new LineRenderer(renderer);
			uiOverlay = new UiOverlay(spriteRenderer);
		}

		void DrawSpriteList(RectangleF rect,
			IEnumerable<Tuple<Sprite, float2, int>> images)
		{
			foreach (var image in images)
			{
				var loc = image.b;

				if (loc.X > rect.Right || loc.X < rect.Left - image.a.bounds.Width)
					continue;
				if (loc.Y > rect.Bottom || loc.Y < rect.Top - image.a.bounds.Height)
					continue;

				spriteRenderer.DrawSprite(image.a, loc, image.c);
			}
		}

		public void Draw()
		{
			terrainRenderer.Draw(Game.viewport);

			var rect = new RectangleF(
				Game.viewport.Location.ToPointF(),
				new SizeF( Game.viewport.Width, Game.viewport.Height ));

			foreach (Actor a in Game.world.Actors.OrderBy(u => u.CenterLocation.Y))
				DrawSpriteList(rect, a.Render());

			foreach (var a in Game.world.Actors
				.Where(u => u.traits.Contains<Traits.RenderWarFactory>())
				.Select(u => u.traits.Get<Traits.RenderWarFactory>()))
				DrawSpriteList(rect, a.RenderRoof(a.self));

			foreach (var e in Game.world.Effects)
				DrawSpriteList(rect, e.Render());

			uiOverlay.Draw();

			spriteRenderer.Flush();

			var selbox = Game.controller.SelectionBox;
			if (selbox != null)
			{
				var a = selbox.Value.First;
				var b = new float2(selbox.Value.Second.X - a.X, 0);
				var c = new float2(0, selbox.Value.Second.Y - a.Y);

				lineRenderer.DrawLine(a, a + b, Color.White, Color.White);
				lineRenderer.DrawLine(a + b, a + b + c, Color.White, Color.White);
				lineRenderer.DrawLine(a + b + c, a + c, Color.White, Color.White);
				lineRenderer.DrawLine(a, a + c, Color.White, Color.White);

				foreach (var u in Game.SelectActorsInBox(selbox.Value.First, selbox.Value.Second))
					DrawSelectionBox(u, Color.Yellow, false);
			}

			if (Game.controller.orderGenerator != null)
				Game.controller.orderGenerator.Render();

			lineRenderer.Flush();
			spriteRenderer.Flush();
		}

		public void DrawSelectionBox(Actor selectedUnit, Color c, bool drawHealthBar)
		{
			var center = selectedUnit.CenterLocation;
			var size = selectedUnit.SelectedSize;

			var xy = center - 0.5f * size;
			var XY = center + 0.5f * size;
			var Xy = new float2(XY.X, xy.Y);
			var xY = new float2(xy.X, XY.Y);

			lineRenderer.DrawLine(xy, xy + new float2(4, 0), c, c);
			lineRenderer.DrawLine(xy, xy + new float2(0, 4), c, c);
			lineRenderer.DrawLine(Xy, Xy + new float2(-4, 0), c, c);
			lineRenderer.DrawLine(Xy, Xy + new float2(0, 4), c, c);

			lineRenderer.DrawLine(xY, xY + new float2(4, 0), c, c);
			lineRenderer.DrawLine(xY, xY + new float2(0, -4), c, c);
			lineRenderer.DrawLine(XY, XY + new float2(-4, 0), c, c);
			lineRenderer.DrawLine(XY, XY + new float2(0, -4), c, c);

			if (drawHealthBar)
			{
				DrawHealthBar(selectedUnit, xy, Xy);

				// Only display pips and tags to the owner
				if (selectedUnit.Owner == Game.LocalPlayer)
				{
					DrawPips(selectedUnit, xY);
					DrawTags(selectedUnit, new float2(center.X, xy.Y));
				}
			}	

			if (ShowUnitPaths)
			{
				var mobile = selectedUnit.traits.GetOrDefault<Mobile>();
				if (mobile != null)
				{
					var path = mobile.GetCurrentPath();
					var start = selectedUnit.Location;

					foreach (var step in path)
					{
						lineRenderer.DrawLine(
							Game.CellSize * start + new float2(12, 12),
							Game.CellSize * step + new float2(12, 12),
							Color.Red, Color.Red);
						start = step;
					}
				}
			}
		}

		void DrawHealthBar(Actor selectedUnit, float2 xy, float2 Xy)
		{
			var c = Color.Gray;
			lineRenderer.DrawLine(xy + new float2(0, -2), xy + new float2(0, -4), c, c);
			lineRenderer.DrawLine(Xy + new float2(0, -2), Xy + new float2(0, -4), c, c);

			var healthAmount = (float)selectedUnit.Health / selectedUnit.Info.Strength;
			var healthColor = (healthAmount < Rules.General.ConditionRed) ? Color.Red
				: (healthAmount < Rules.General.ConditionYellow) ? Color.Yellow
				: Color.LimeGreen;

			var healthColor2 = Color.FromArgb(
				255,
				healthColor.R / 2,
				healthColor.G / 2,
				healthColor.B / 2);

			var z = float2.Lerp(xy, Xy, healthAmount);

			lineRenderer.DrawLine(z + new float2(0, -4), Xy + new float2(0, -4), c, c);
			lineRenderer.DrawLine(z + new float2(0, -2), Xy + new float2(0, -2), c, c);

			lineRenderer.DrawLine(xy + new float2(0, -3), z + new float2(0, -3), healthColor, healthColor);
			lineRenderer.DrawLine(xy + new float2(0, -2), z + new float2(0, -2), healthColor2, healthColor2);
			lineRenderer.DrawLine(xy + new float2(0, -4), z + new float2(0, -4), healthColor2, healthColor2);
		}

		// depends on the order of pips in TraitsInterfaces.cs!
		static readonly string[] pipStrings = { "pip-empty", "pip-green", "pip-yellow", "pip-red", "pip-gray" };
		static readonly string[] tagStrings = { "", "tag-fake", "tag-primary" };

		void DrawPips(Actor selectedUnit, float2 basePosition)
		{
			// If a mod wants to implement a unit with multiple pip sources, then they are placed on multiple rows
			var pipxyBase = basePosition + new float2(-12, -7); // Correct for the offset in the shp file
			var pipxyOffset = new float2(0, 0); // Correct for offset due to multiple columns/rows

			foreach (var pips in selectedUnit.traits.WithInterface<IPips>())
			{
				foreach (var pip in pips.GetPips())
				{
					var pipImages = new Animation("pips");
					pipImages.PlayRepeating(pipStrings[(int)pip]);
					spriteRenderer.DrawSprite(pipImages.Image, pipxyBase + pipxyOffset, 0);
					pipxyOffset += new float2(4, 0);
					
					if (pipxyOffset.X+5 > selectedUnit.SelectedSize.X)
					{
						//spriteRenderer.Flush();
						pipxyOffset.X = 0;
						pipxyOffset.Y -= 4;
					}
				}
				// Increment row
				pipxyOffset.X = 0;
				pipxyOffset.Y -= 5;
				//spriteRenderer.Flush();
			}
		}
		
		void DrawTags(Actor selectedUnit, float2 basePosition)
		{
			// If a mod wants to implement a unit with multiple tags, then they are placed on multiple rows
			var tagxyBase = basePosition + new float2(-16, 2); // Correct for the offset in the shp file
			var tagxyOffset = new float2(0, 0); // Correct for offset due to multiple rows

			foreach (var tags in selectedUnit.traits.WithInterface<ITags>())
			{
				foreach (var tag in tags.GetTags())
				{
					var tagImages = new Animation("pips");
					tagImages.PlayRepeating(tagStrings[(int)tag]);
					spriteRenderer.DrawSprite(tagImages.Image, tagxyBase + tagxyOffset, 0);
					
					// Increment row
					tagxyOffset.Y += 8;
				}
			}
		}
	}
}
