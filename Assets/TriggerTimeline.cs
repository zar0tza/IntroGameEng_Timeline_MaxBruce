using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Animations;
using UnityEngine.EventSystems;
using UnityEngine.Timeline;

public class TriggerTimeline : MonoBehaviour
{
    public PlayableDirector timeline;
    public GameObject animator;
    public GameObject animator2;



    private void Start()
    {
        animator.GetComponent<Animator>().enabled = false;
        animator2.GetComponent<Animator>().enabled = false;
    }

    void OnTriggerEnter(Collider other)
    {
        animator.GetComponent<Animator>().enabled = true;
        animator2.GetComponent<Animator>().enabled = true;
        timeline.Play();
    }


   
}