using UnityEngine;

public class KeyboardPlayerController : MonoBehaviour
{
    [SerializeField] bool enableKeyboardControl = true;
    [SerializeField] float moveSpeed = 2f;
    [SerializeField] float verticalSpeed = 1.2f;
    [SerializeField] float turnSpeed = 90f;
    [SerializeField] Transform directionReference;

    CharacterController characterController;

    public void SetDirectionReference(Transform reference)
    {
        directionReference = reference;
    }

    void Awake()
    {
        characterController = GetComponent<CharacterController>();

        if (directionReference == null && Camera.main != null)
        {
            directionReference = Camera.main.transform;
        }
    }

    void Update()
    {
        if (!enableKeyboardControl)
        {
            return;
        }

        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        float upDown = 0f;

        if (Input.GetKey(KeyCode.Space))
        {
            upDown += 1f;
        }

        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        {
            upDown -= 1f;
        }

        float turn = 0f;
        if (Input.GetKey(KeyCode.Q))
        {
            turn -= 1f;
        }

        if (Input.GetKey(KeyCode.E))
        {
            turn += 1f;
        }

        transform.Rotate(Vector3.up, turn * turnSpeed * Time.deltaTime, Space.World);

        Vector3 forward = directionReference != null ? directionReference.forward : transform.forward;
        Vector3 right = directionReference != null ? directionReference.right : transform.right;
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        Vector3 planarMovement = (forward * vertical + right * horizontal).normalized * moveSpeed;
        Vector3 movement = planarMovement + Vector3.up * (upDown * verticalSpeed);
        movement *= Time.deltaTime;

        if (characterController != null)
        {
            characterController.Move(movement);
        }
        else
        {
            transform.position += movement;
        }
    }
}
