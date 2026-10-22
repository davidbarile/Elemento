#import <UIKit/UIKit.h>

extern UIViewController* UnityGetGLViewController();

extern "C" void ElementoShareText(const char* inText)
{
    UIViewController* root = UnityGetGLViewController();
    UIActivityViewController* controller = [[UIActivityViewController alloc]
        initWithActivityItems:@[[NSString stringWithUTF8String:inText]] applicationActivities:nil];
    // iPad presents the share sheet as a popover, which crashes without an anchor.
    controller.popoverPresentationController.sourceView = root.view;
    controller.popoverPresentationController.sourceRect =
        CGRectMake(CGRectGetMidX(root.view.bounds), CGRectGetMidY(root.view.bounds), 0, 0);
    controller.popoverPresentationController.permittedArrowDirections = 0;
    [root presentViewController:controller animated:YES completion:nil];
}
