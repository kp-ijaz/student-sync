import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

/// Fade with a slight zoom, for screens that replace each other rather than stack
/// (splash → sign-in → home after a redirect), where a sideways slide would feel wrong.
CustomTransitionPage<void> fadePage(GoRouterState state, Widget child) => CustomTransitionPage<void>(
      key: state.pageKey,
      child: child,
      transitionDuration: const Duration(milliseconds: 320),
      reverseTransitionDuration: const Duration(milliseconds: 220),
      transitionsBuilder: (context, animation, _, child) {
        final curved = CurvedAnimation(parent: animation, curve: Curves.easeOutCubic);
        return FadeTransition(
          opacity: curved,
          child: ScaleTransition(scale: Tween<double>(begin: 0.98, end: 1).animate(curved), child: child),
        );
      },
    );

/// Keeps every tab alive (like an IndexedStack) and fades the newly selected tab in
/// with a small upward drift, so switching tabs feels smooth instead of abrupt.
class FadeIndexedStack extends StatefulWidget {
  const FadeIndexedStack({super.key, required this.index, required this.children});
  final int index;
  final List<Widget> children;

  @override
  State<FadeIndexedStack> createState() => _FadeIndexedStackState();
}

class _FadeIndexedStackState extends State<FadeIndexedStack> with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(vsync: this, duration: const Duration(milliseconds: 240), value: 1);
  late final Animation<double> _fade = CurvedAnimation(parent: _controller, curve: Curves.easeOutCubic);
  late final Animation<Offset> _slide = Tween(begin: const Offset(0, 0.015), end: Offset.zero).animate(_fade);

  @override
  void didUpdateWidget(FadeIndexedStack old) {
    super.didUpdateWidget(old);
    if (old.index != widget.index) _controller.forward(from: 0);
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => IndexedStack(
        index: widget.index,
        children: [
          // Same wrapper for every tab so switching never rebuilds (and resets) a tab.
          for (var i = 0; i < widget.children.length; i++)
            FadeTransition(
              opacity: i == widget.index ? _fade : kAlwaysCompleteAnimation,
              child: SlideTransition(position: i == widget.index ? _slide : const AlwaysStoppedAnimation(Offset.zero), child: widget.children[i]),
            ),
        ],
      );
}
