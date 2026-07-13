using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using AldenFall.UI;

namespace AldenFall.Tests
{
    /// <summary>
    /// Visual QA: captures the live HUD in playmode (combat state + open panels)
    /// to PNGs under Screenshots/. Assertions are minimal — the point is the pixels.
    /// </summary>
    public class HUDCaptureTest
    {
        [UnityTest]
        public IEnumerator CaptureHUDScreens()
        {
            SceneManager.LoadScene("EmberfallDemo");
            yield return null;
            yield return new WaitForSeconds(0.8f);

            var cam = Camera.main;
            Assert.IsNotNull(cam, "no camera");

            // Route the overlay canvas through the camera so RT capture sees it
            var canvas = GameObject.Find("GameUI")?.GetComponent<Canvas>();
            Assert.IsNotNull(canvas, "no GameUI canvas");
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 0.5f;

            System.IO.Directory.CreateDirectory("Screenshots");

            // 1) Combat state: target a goblin so the target frame + numbers show
            var player = GameObject.FindGameObjectWithTag("Player");
            var targeting = player.GetComponent<Combat.TargetingSystem>();
            var enemy = Object.FindFirstObjectByType<AI.EnemyAI>();
            if (enemy != null)
            {
                targeting.SetTarget(enemy.transform);
                var enemyHealth = enemy.GetComponent<Combat.Health>();
                enemyHealth.TakeDamage(12f, player); // damage number + hp lag visible
            }
            yield return new WaitForSeconds(0.35f);
            Capture(cam, "Screenshots/hud_combat.png");

            // 2) Panels open
            var hud = Object.FindFirstObjectByType<GameHUD>();
            Debug.Log($"HUDCAP inventoryPanel={(hud.inventoryPanel != null)} skillsPanel={(hud.skillsPanel != null)}");
            hud.inventoryPanel?.Set(true);
            hud.skillsPanel?.Set(true);
            yield return new WaitForSeconds(0.5f);

            var skillsObj = GameObject.Find("SkillsPanel");
            if (skillsObj != null)
            {
                var rect = skillsObj.GetComponent<RectTransform>();
                var group = skillsObj.GetComponent<CanvasGroup>();
                Debug.Log($"HUDCAP SkillsPanel pos={rect.anchoredPosition} alpha={(group != null ? group.alpha : -1f)} " +
                          $"activeSelf={skillsObj.activeSelf} childCount={skillsObj.transform.childCount}");
            }
            else Debug.Log("HUDCAP SkillsPanel OBJECT NOT FOUND IN SCENE");

            Capture(cam, "Screenshots/hud_panels.png");

            // 3) Combat trees panel (close the others so it reads clearly)
            hud.inventoryPanel?.Set(false);
            hud.skillsPanel?.Set(false);
            var trees = Object.FindFirstObjectByType<SkillTreePanelUI>(FindObjectsInactive.Include);
            if (trees != null)
                trees.GetComponent<SlidePanel>()?.Set(true);
            yield return new WaitForSeconds(0.5f);
            Capture(cam, "Screenshots/hud_trees.png");
        }

        private static void Capture(Camera cam, string path)
        {
            var rt = new RenderTexture(1600, 900, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(rt);
            Object.Destroy(tex);
        }
    }
}
