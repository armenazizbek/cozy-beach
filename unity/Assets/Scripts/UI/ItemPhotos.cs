using System;
using System.Collections.Generic;
using System.Linq;

// Port of src/ui/ItemPhotos.ts

public static class ItemPhotos
{
    private const int SIZE = 168;

    /// <summary>
    /// Takes a little photograph of each find, for the polaroids on the Lost & Found board. It uses a renderer of its own, once, while the
    /// game loads. Returns an image for each id; if the photos cannot be taken the map is simply empty.
    /// </summary>
    public static Dictionary<string, UnityEngine.Texture> takeItemPhotos(IEnumerable<string> ids)
    {
        var photos = new Dictionary<string, UnityEngine.Texture>();
        THREE.WebGLRenderer renderer = null;
        try
        {
            renderer = new THREE.WebGLRenderer(new THREE.WebGLRendererParameters { antialias = true, preserveDrawingBuffer = true });
            renderer.setSize(SIZE, SIZE, false);
            renderer.toneMapping = THREE.ToneMapping.ACESFilmicToneMapping;
            var scene = new THREE.Scene { name = "Item photos" };
            scene.background = new THREE.Color(0xd9c6a3);
            var sun = new THREE.DirectionalLight(0xfff2dc, 3);
            sun.position.set(2, 4, 3);
            scene.add(new THREE.HemisphereLight(0xffffff, 0xb59a70, 1.6), sun);
            var camera = new THREE.PerspectiveCamera(28, 1, 0.01, 100);
            var box = new THREE.Box3();
            var middle = new THREE.Vector3();
            var size = new THREE.Vector3();
            foreach (var id in ids.Distinct())
            {
                var item = LootGenerator.buildItem(id);
                scene.add(item);
                box.setFromObject(item).getCenter(middle);
                double reach = box.getSize(size).length() / 2;
                // from a little above, the way you would photograph something on a table, and close enough to fill the frame
                camera.position.copy(middle).add(new THREE.Vector3(0.45, 0.8, 0.95).normalize().multiplyScalar(reach / Math.Sin(THREE.MathUtils.degToRad(14)) * 1.02));
                camera.lookAt(middle);
                renderer.render(scene, camera);
                photos[id] = renderer.domElement.toDataURL("image/jpeg", 0.85);
                scene.remove(item);
                item.traverse(o => { if (o is THREE.Mesh m) m.geometry.dispose(); });
            }
        }
        catch (Exception e)
        {
            /* the board reads fine without pictures */
            UnityEngine.Debug.LogWarning("Item photos skipped: " + e.Message);
        }
        renderer?.dispose();
        return photos;
    }
}
