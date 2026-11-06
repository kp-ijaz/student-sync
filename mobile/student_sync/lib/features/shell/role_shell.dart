import 'dart:ui';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:go_router/go_router.dart';

import '../../core/theme/tokens.dart';

class ShellTab {
  const ShellTab(this.path, this.label, this.icon, this.screen);
  final String path;
  final String label;
  final IconData icon;
  final Widget screen;
}

/// Floating bottom navigation for a role (at most four tabs). Each tab keeps its own
/// navigation state. The body extends behind the bar, so scrolling content passes under
/// the frosted glass; `pagePadding` reads the bar's height from the bottom media padding.
class RoleShell extends StatelessWidget {
  const RoleShell({super.key, required this.shell, required this.tabs}) : assert(tabs.length <= 4, 'The bottom bar holds at most four tabs.');
  final StatefulNavigationShell shell;
  final List<ShellTab> tabs;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      extendBody: true,
      body: shell,
      bottomNavigationBar: FloatingNavBar(
        tabs: tabs,
        selectedIndex: shell.currentIndex,
        onSelected: (i) {
          if (i != shell.currentIndex) HapticFeedback.selectionClick();
          // Tapping the current tab returns it to its first screen.
          shell.goBranch(i, initialLocation: i == shell.currentIndex);
        },
      ),
    );
  }
}

class FloatingNavBar extends StatelessWidget {
  const FloatingNavBar({super.key, required this.tabs, required this.selectedIndex, required this.onSelected});
  final List<ShellTab> tabs;
  final int selectedIndex;
  final ValueChanged<int> onSelected;

  static const _height = 72.0;

  @override
  Widget build(BuildContext context) {
    final radius = BorderRadius.circular(SsRadius.sheet);
    return SafeArea(
      top: false,
      minimum: const EdgeInsets.fromLTRB(SsSpace.s4, 0, SsSpace.s4, SsSpace.s3),
      child: DecoratedBox(
        decoration: BoxDecoration(borderRadius: radius, boxShadow: SsShadow.floating),
        child: ClipRRect(
          borderRadius: radius,
          child: BackdropFilter(
            filter: ImageFilter.blur(sigmaX: 24, sigmaY: 24),
            child: Container(
              height: _height,
              padding: const EdgeInsets.all(SsSpace.s2),
              decoration: BoxDecoration(
                color: SsColors.white.withValues(alpha: 0.92),
                borderRadius: radius,
                border: Border.all(color: SsColors.white.withValues(alpha: 0.6)),
              ),
              child: Semantics(
                container: true,
                explicitChildNodes: true,
                label: 'Main navigation',
                child: Row(
                  children: [
                    for (var i = 0; i < tabs.length; i++) ...[
                      if (i > 0) const SizedBox(width: SsSpace.s2),
                      // The selected tab grows to show its label.
                      Expanded(
                        flex: i == selectedIndex ? 2 : 1,
                        child: _NavItem(tab: tabs[i], selected: i == selectedIndex, onTap: () => onSelected(i), index: i, count: tabs.length),
                      ),
                    ],
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _NavItem extends StatelessWidget {
  const _NavItem({required this.tab, required this.selected, required this.onTap, required this.index, required this.count});
  final ShellTab tab;
  final bool selected;
  final VoidCallback onTap;
  final int index;
  final int count;

  @override
  Widget build(BuildContext context) {
    final duration = MediaQuery.disableAnimationsOf(context) ? Duration.zero : SsMotion.medium;
    final radius = BorderRadius.circular(SsRadius.card);
    return Tooltip(
      message: tab.label,
      excludeFromSemantics: true,
      child: Semantics(
        button: true,
        selected: selected,
        label: '${tab.label}, tab ${index + 1} of $count',
        excludeSemantics: true,
        child: Material(
          type: MaterialType.transparency,
          child: InkWell(
            onTap: onTap,
            borderRadius: radius,
            child: AnimatedContainer(
              duration: duration,
              curve: SsMotion.curve,
              decoration: BoxDecoration(
                color: selected ? SsColors.navy : SsColors.fill,
                borderRadius: radius,
                boxShadow: selected ? const [BoxShadow(color: Color(0x330B2854), blurRadius: 12, offset: Offset(0, 4))] : null,
              ),
              child: Center(
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Icon(tab.icon, size: 20, color: selected ? SsColors.white : SsColors.navy),
                    // Only the selected tab shows its label; the others are icons with tooltips.
                    // Flexible + clip: the label may be squeezed while the pill animates open.
                    Flexible(
                      child: ClipRect(
                        child: AnimatedSize(
                          duration: duration,
                          curve: SsMotion.curve,
                          child: selected
                              ? Padding(
                                  padding: const EdgeInsets.only(left: SsSpace.s2),
                                  child: ConstrainedBox(
                                    constraints: const BoxConstraints(maxWidth: 96),
                                    child: FittedBox(
                                      fit: BoxFit.scaleDown,
                                      child: Text(
                                        tab.label,
                                        maxLines: 1,
                                        softWrap: false,
                                        style: const TextStyle(fontFamily: 'Inter', fontSize: 14, fontWeight: FontWeight.w700, color: SsColors.white),
                                      ),
                                    ),
                                  ),
                                )
                              : const SizedBox.shrink(),
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
