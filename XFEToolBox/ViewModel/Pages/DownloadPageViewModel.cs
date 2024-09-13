using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XFEToolBox.Profiles.CrossVersionProfiles;
using XFEToolBox.Resources.Resource;
using XFEToolBox.Utilities;
using XFEToolBox.Views.Controls;
using XFEToolBox.Views.Pages;
using XFEToolBox.Views.Pages.Popups;

namespace XFEToolBox.ViewModel.Pages;

public partial class DownloadPageViewModel(DownloadPage viewPage) : ObservableObject
{
    public DownloadPage ViewPage { get; set; } = viewPage;

    [RelayCommand]
    void GotoDownloadPage(MiniToolButton miniToolButton)
    {
        if (!DownloadProfile.DownloadAgreementAccepted)
        {
            var result = PopupHelper.ShowDialog(new AgreementDialogPopupPage()
            {
                Title = "下载协议同意书",
                Agreement = """
                软件免责协议：


                1. 免责声明

                    本软件（以下简称“本软件”）仅用于提供下载服务。用户使用本软件下载的任何内容，均由用户自行负责。本软件的开发者（以下简称“开发者”）对用户使用本软件下载的内容的合法性、准确性、安全性及任何其他形式的风险不承担任何责任。


                2. 不提供担保

                    本软件按照“现状”提供，没有任何形式的担保，无论是明示的还是暗示的。开发者不对本软件的功能、运行或可用性做出任何保证。开发者不保证本软件无故障、无病毒或其他有害组件。


                3. 责任限制

                    在适用法律允许的最大范围内，开发者对因使用或无法使用本软件所引起的任何直接、间接、偶然、特殊、或后果性的损害（包括但不限于数据丢失、业务中断、经济损失等）不承担任何责任，即使开发者已被告知此类损害的可能性。


                4. 用户责任

                    用户在使用本软件时，应遵守相关法律法规和政策。用户不得利用本软件从事任何非法活动或侵犯第三方权利的行为。因用户不当使用本软件造成的所有后果，均由用户自行承担。


                5. 协议的接受

                    通过安装、复制或以其他方式使用本软件，用户表示已阅读、理解并同意受本免责协议的条款约束。如果用户不同意本免责协议的条款，请勿安装或使用本软件。
                """
            }, 480, 420);
            if (result == System.Windows.MessageBoxResult.Yes)
                DownloadProfile.DownloadAgreementAccepted = true;
        }
        if (DownloadProfile.DownloadAgreementAccepted && miniToolButton.Tag is string gotoUrl)
        {
            var urlString = DownloadInfoResource.ResourceManager.GetString(gotoUrl);
        }
    }
}
