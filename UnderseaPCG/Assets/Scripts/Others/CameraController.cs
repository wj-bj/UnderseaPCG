using System.Collections;
using System.Collections.Generic;
using Unity.Entities.UniversalDelegates;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    
    public Transform target;
    public Transform lookAtTarget;
    public Vector3 followOffset;
    public float lookAheadDst = 10;
    public float smoothTime = .1f;
    public float rotSmoothSpeed = 3;

    Vector3 smoothV;

    public float sensitivity = 100f; // 控制旋转灵敏度

    private float xRotation = 0f; // 用于累计垂直方向上的旋转
    private float yRotation = 0f;

    
    void LateUpdate()
    {
        if(target == null)
            return;

        Vector3 targetPos = target.position + target.forward * followOffset.z + target.up * followOffset.y + target.right * followOffset.x;
        transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref smoothV, smoothTime);

   

        if (Input.GetMouseButton(0))
        {
                // 获取鼠标输入
            float mouseX = Input.GetAxis("Mouse X") * sensitivity * Time.deltaTime;
            float mouseY = Input.GetAxis("Mouse Y") * sensitivity * Time.deltaTime;
            xRotation = 0f;
            yRotation = 0f;
            xRotation -= mouseY;
            xRotation = Mathf.Clamp(xRotation, -90f, 90f); 
            yRotation -= mouseX;
            yRotation = Mathf.Clamp(yRotation, -90f, 90f); 

            // 应用旋转到摄像头和玩家身体
            var cRotate = transform.localRotation;
            var vRotate = cRotate.eulerAngles;
            transform.localRotation = Quaternion.Euler(vRotate.x+xRotation, vRotate.y+yRotation, 0f);
           
            // playerBody.Rotate(Vector3.up * mouseX);

        }
        else
        {
                if(lookAtTarget != null){
                                    
                    var lookAtDirecton = lookAtTarget.position - transform.position;
                    
                    transform.LookAt(target.position + lookAtDirecton.normalized * lookAheadDst);
                }
                else{
                     
                    transform.LookAt(target.position + target.right * lookAheadDst);
                }
                Quaternion rot = transform.rotation;
                Quaternion targetRot = transform.rotation;

                transform.rotation = Quaternion.Slerp(rot,targetRot,Time.deltaTime * rotSmoothSpeed);

        }
    }
 

    void Start()
    {
        
       
    }

    void Update()
    {

       
    }
}
