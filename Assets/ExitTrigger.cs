using UnityEngine;

public class ExitTrigger : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        Application.Quit();
    }
}
