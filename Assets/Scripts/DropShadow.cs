using UnityEngine;

/// <summary>
/// Creates a hard 2D drop shadow by duplicating the SpriteRenderer on a child object.
/// </summary>
public class DropShadow : MonoBehaviour
{
    [Header("Settings")]
    public Vector2 offset = new Vector2(0.1f, -0.1f);
    public Color shadowColor = new Color(0, 0, 0, 0.5f);
    public string shadowSortingLayer = "Default";
    public int shadowSortingOrder = -1; // Behind the main sprite
    public Material shadowLineMaterial;

    private SpriteRenderer mainRenderer;
    private SpriteRenderer shadowRenderer;
    
    private LineRenderer mainLine;
    private LineRenderer shadowLine;

    private GameObject shadowObj;

    void Awake()
    {
        mainRenderer = GetComponent<SpriteRenderer>();
        mainLine     = GetComponent<LineRenderer>();

        if (mainRenderer == null && mainLine == null) return;

        CreateShadow();
    }

    void CreateShadow()
    {
        shadowObj = new GameObject(gameObject.name + "_Shadow");
        shadowObj.transform.SetParent(transform);
        shadowObj.transform.localPosition = (Vector3)offset + new Vector3(0, 0, 0.1f);
        shadowObj.transform.localRotation = Quaternion.identity;
        shadowObj.transform.localScale = Vector3.one;

        if (mainRenderer != null)
        {
            shadowRenderer = shadowObj.AddComponent<SpriteRenderer>();
        }
        
        if (mainLine != null)
        {
            shadowLine = shadowObj.AddComponent<LineRenderer>();
            shadowLine.useWorldSpace = mainLine.useWorldSpace;
            shadowLine.textureMode   = mainLine.textureMode;
            shadowLine.widthCurve    = mainLine.widthCurve;
            shadowLine.widthMultiplier = mainLine.widthMultiplier;
            
            if (shadowLineMaterial != null)
                shadowLine.material = shadowLineMaterial;
            else
                shadowLine.material = new Material(Shader.Find("Sprites/Default"));
        }

        SyncShadow();
    }

    void LateUpdate()
    {
        if (mainRenderer != null && shadowRenderer != null)
        {
            SyncShadow();
        }
    }

    void SyncShadow()
    {
        if (mainRenderer != null && shadowRenderer != null)
        {
            shadowRenderer.sprite = mainRenderer.sprite;
            shadowRenderer.color = shadowColor;
            shadowRenderer.sortingLayerName = shadowSortingLayer;
            shadowRenderer.sortingOrder = mainRenderer.sortingOrder - 1;
            
            // Match flip
            shadowRenderer.flipX = mainRenderer.flipX;
            shadowRenderer.flipY = mainRenderer.flipY;
        }

        if (mainLine != null && shadowLine != null)
        {
            shadowLine.positionCount = mainLine.positionCount;
            
            // Sync points with offset
            for (int i = 0; i < mainLine.positionCount; i++)
            {
                Vector3 worldPos = mainLine.GetPosition(i);
                // The shadow object is already offset via its localPosition, 
                // but LineRenderer positions are world space if useWorldSpace is true.
                if (mainLine.useWorldSpace)
                {
                    shadowLine.SetPosition(i, worldPos + (Vector3)offset);
                }
                else
                {
                    shadowLine.SetPosition(i, worldPos); // Local space handles it via shadowObj transform
                }
            }

            shadowLine.startColor = shadowColor;
            shadowLine.endColor   = shadowColor;
            shadowLine.sortingLayerName = shadowSortingLayer;
            shadowLine.sortingOrder = mainLine.sortingOrder - 1;
        }
    }
}
