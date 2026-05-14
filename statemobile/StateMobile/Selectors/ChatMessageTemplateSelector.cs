using StateMobile.Models;

namespace StateMobile.Selectors
{
    public class ChatMessageTemplateSelector : DataTemplateSelector
    {
        public DataTemplate NormalMessageTemplate { get; set; }
        public DataTemplate SystemMessageTemplate { get; set; }

        protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
        {
            if (item is ChatMessage message && message.SenderID == "SYSTEM")
            {
                return SystemMessageTemplate;
            }
            return NormalMessageTemplate;
        }
    }
}
