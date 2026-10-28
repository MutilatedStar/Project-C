using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class BouncingSprite : MonoBehaviour
{
    [Header("Movement")]
    public Vector2 velocity = new Vector2(3f, 2f);

    [Header("Sprite")]
    [Tooltip("Tick this if your sprite artwork faces right by default.")]
    public bool spriteFacesRight = true;

    private SpriteRenderer sr;
    private Camera cam;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        cam = Camera.main;
    }

    void Update()
    {
        // Move
        transform.position += (Vector3)(velocity * Time.deltaTime);

        // Work out the screen edges (requires an Orthographic camera)
        float halfH = cam.orthographicSize;
        float halfW = halfH * cam.aspect;
        Vector3 camPos = cam.transform.position;
        Vector3 ext = sr.bounds.extents;

        float minX = camPos.x - halfW + ext.x;
        float maxX = camPos.x + halfW - ext.x;
        float minY = camPos.y - halfH + ext.y;
        float maxY = camPos.y + halfH - ext.y;

        Vector3 pos = transform.position;

        // Bounce off the left/right edges
        if (pos.x <= minX) { pos.x = minX; velocity.x = Mathf.Abs(velocity.x); }
        else if (pos.x >= maxX) { pos.x = maxX; velocity.x = -Mathf.Abs(velocity.x); }

        // Bounce off the top/bottom edges
        if (pos.y <= minY) { pos.y = minY; velocity.y = Mathf.Abs(velocity.y); }
        else if (pos.y >= maxY) { pos.y = maxY; velocity.y = -Mathf.Abs(velocity.y); }

        transform.position = pos;

        // Flip to face the direction of travel
        if (velocity.x != 0f)
        {
            bool movingLeft = velocity.x < 0f;
            sr.flipX = spriteFacesRight ? movingLeft : !movingLeft;
        }
    }
}
