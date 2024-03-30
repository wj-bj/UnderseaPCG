using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class UISliderKeyboardDisable : MonoBehaviour
{
   public Slider slider; // 引用你的Slider

    void Update()
    {
        // 如果当前有对象被选中，并且那个对象是我们的滑块
        if (EventSystem.current.currentSelectedGameObject == slider.gameObject)
        {
            // 取消选中
            EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
