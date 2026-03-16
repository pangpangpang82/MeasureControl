using System;
using MeasureControl.Drivers;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.TestTask.CardCATPanel.MIL1394B
{
    /// <summary>
    /// 1394B板卡面板ViewModel
    /// </summary>
    public class Mil1394CardPanelViewModel : BindableBase
    {
        private readonly uint _cardNum;
        private readonly uint _nodeCount;
        private readonly IntPtr[] _pnode;

        public Mil1394CardPanelViewModel(uint cardNum, uint nodeCount, IntPtr[] pnode)
        {
            _cardNum = cardNum;
            _nodeCount = nodeCount;
            _pnode = pnode ?? throw new ArgumentNullException(nameof(pnode));
        }
    }
}
