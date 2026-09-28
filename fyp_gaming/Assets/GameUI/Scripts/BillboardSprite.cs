using UnityEngine;

public class BillboardSprite : MonoBehaviour
{
    public float pitch = 45f;

    void LateUpdate()
    {
        transform.rotation = Quaternion.Euler(pitch, 0f, 0f);
    }
}
