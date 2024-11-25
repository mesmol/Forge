using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayPuff : MonoBehaviour
{
    [SerializeField] private ParticleSystem PuffFX;

    private Animator oakAc;
    // Start is called before the first frame update
    void Start()
    {
        oakAc = GetComponent<Animator>();
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            oakAc.SetTrigger("Fall");
        }
    }

    public void PlayFX()
    {
        PuffFX.Play();
    }
}
