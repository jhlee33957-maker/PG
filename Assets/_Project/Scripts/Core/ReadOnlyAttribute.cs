using UnityEngine;

namespace CoffeeSpec.Core
{
    /// <summary>
    /// 인스펙터에 값은 보여 주지만 수정은 못 하게 한다.
    /// 코드가 자동으로 채우는 값을 사람이 실수로 바꾸지 않도록 쓴다. 그리는 쪽은 Editor/ReadOnlyDrawer.cs.
    /// </summary>
    public sealed class ReadOnlyAttribute : PropertyAttribute
    {
    }
}