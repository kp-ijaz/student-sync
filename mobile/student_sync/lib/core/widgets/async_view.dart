import 'package:flutter/material.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../api/api_exception.dart';
import '../theme/tokens.dart';
import 'states.dart';

/// Renders exactly one of: loading, error (with Retry), forbidden, empty, content.
///
/// The builder receives `reload`, which keeps the current content on screen while
/// refreshing (pull-to-refresh) and reports refresh failures in a snackbar.
class AsyncView<T> extends StatefulWidget {
  const AsyncView({
    super.key,
    required this.load,
    required this.builder,
    this.isEmpty,
    this.empty,
    this.loadingLabel = 'Loading',
  });

  final Future<T> Function() load;
  final Widget Function(BuildContext context, T data, Future<void> Function() reload) builder;
  final bool Function(T data)? isEmpty;
  final Widget Function(BuildContext context, Future<void> Function() reload)? empty;
  final String loadingLabel;

  @override
  State<AsyncView<T>> createState() => AsyncViewState<T>();
}

class AsyncViewState<T> extends State<AsyncView<T>> {
  T? _data;
  bool _hasData = false;
  Object? _error;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _initialLoad();
  }

  Future<void> _initialLoad() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final data = await widget.load();
      if (!mounted) return;
      setState(() {
        _data = data;
        _hasData = true;
        _loading = false;
      });
    } on Object catch (e) {
      if (!mounted) return;
      if (e is ApiException && e.code == 'session_expired') return; // router takes over
      setState(() {
        _error = e;
        _loading = false;
      });
    }
  }

  /// Refreshes in place. Falls back to a full load when there is no content yet.
  Future<void> reload() async {
    if (!_hasData) return _initialLoad();
    try {
      final data = await widget.load();
      if (!mounted) return;
      setState(() => _data = data);
    } on Object catch (e) {
      if (!mounted) return;
      if (e is ApiException && e.code == 'session_expired') return;
      showSnack(context, describeError(e), error: true);
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_loading && !_hasData) return LoadingView(label: widget.loadingLabel);
    if (_error != null && !_hasData) return ErrorState(error: _error!, onRetry: _initialLoad);
    final data = _data as T;
    if (widget.isEmpty?.call(data) ?? false) {
      final empty = widget.empty;
      if (empty != null) {
        return RefreshIndicator(
          onRefresh: reload,
          child: LayoutBuilder(
            builder: (context, c) => SingleChildScrollView(
              physics: const AlwaysScrollableScrollPhysics(),
              child: ConstrainedBox(constraints: BoxConstraints(minHeight: c.maxHeight), child: empty(context, reload)),
            ),
          ),
        );
      }
    }
    return widget.builder(context, data, reload);
  }
}

/// One section of a longer page that loads its own data: a small skeleton while
/// loading, an inline error with Retry, nothing (or [empty]) when there is no data,
/// otherwise [builder]. Change the widget's key to reload it.
class AsyncSection<T> extends StatefulWidget {
  const AsyncSection({super.key, required this.load, required this.builder, this.isEmpty, this.empty, this.onLoaded});
  final Future<T> Function() load;
  final Widget Function(BuildContext context, T data, Future<void> Function() reload) builder;
  final bool Function(T data)? isEmpty;

  /// Shown when [isEmpty]; the section disappears when this is null.
  final WidgetBuilder? empty;

  /// Called once after the first successful load (e.g. to scroll to this section).
  final VoidCallback? onLoaded;

  @override
  State<AsyncSection<T>> createState() => _AsyncSectionState<T>();
}

class _AsyncSectionState<T> extends State<AsyncSection<T>> {
  T? _data;
  bool _hasData = false;
  Object? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final data = await widget.load();
      if (!mounted) return;
      final first = !_hasData;
      setState(() {
        _data = data;
        _hasData = true;
        _error = null;
      });
      if (first && widget.onLoaded != null) WidgetsBinding.instance.addPostFrameCallback((_) => widget.onLoaded!());
    } on Object catch (e) {
      if (!mounted) return;
      if (e is ApiException && e.code == 'session_expired') return;
      if (_hasData) {
        showSnack(context, describeError(e), error: true);
      } else {
        setState(() => _error = e);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_error != null && !_hasData) {
      return Container(
        margin: const EdgeInsets.only(top: SsSpace.s3),
        padding: const EdgeInsets.fromLTRB(SsSpace.s4, SsSpace.s2, SsSpace.s2, SsSpace.s2),
        decoration: BoxDecoration(color: SsColors.error.withValues(alpha: 0.06), borderRadius: BorderRadius.circular(SsRadius.card)),
        child: Row(children: [
          const Icon(LucideIcons.circleAlert, size: 18, color: SsColors.error),
          const SizedBox(width: SsSpace.s3),
          Expanded(child: Text(describeError(_error!), style: Theme.of(context).textTheme.bodySmall)),
          TextButton(
            onPressed: () {
              setState(() => _error = null);
              _load();
            },
            child: const Text('Retry'),
          ),
        ]),
      );
    }
    if (!_hasData) return const SectionSkeleton();
    final data = _data as T;
    if (widget.isEmpty?.call(data) ?? false) return widget.empty?.call(context) ?? const SizedBox.shrink();
    return widget.builder(context, data, _load);
  }
}
