"use strict";
var __create = Object.create;
var __defProp = Object.defineProperty;
var __getOwnPropDesc = Object.getOwnPropertyDescriptor;
var __getOwnPropNames = Object.getOwnPropertyNames;
var __getProtoOf = Object.getPrototypeOf;
var __hasOwnProp = Object.prototype.hasOwnProperty;
var __commonJS = (cb, mod) => function __require() {
  try {
    return mod || (0, cb[__getOwnPropNames(cb)[0]])((mod = { exports: {} }).exports, mod), mod.exports;
  } catch (e) {
    throw mod = 0, e;
  }
};
var __copyProps = (to, from, except, desc2) => {
  if (from && typeof from === "object" || typeof from === "function") {
    for (let key of __getOwnPropNames(from))
      if (!__hasOwnProp.call(to, key) && key !== except)
        __defProp(to, key, { get: () => from[key], enumerable: !(desc2 = __getOwnPropDesc(from, key)) || desc2.enumerable });
  }
  return to;
};
var __toESM = (mod, isNodeMode, target) => (target = mod != null ? __create(__getProtoOf(mod)) : {}, __copyProps(
  // If the importer is in node compatibility mode or this is not an ESM
  // file that has been converted to a CommonJS file using a Babel-
  // compatible transform (i.e. "__esModule" has not been set), then set
  // "default" to the CommonJS "module.exports" for node compatibility.
  isNodeMode || !mod || !mod.__esModule ? __defProp(target, "default", { value: mod, enumerable: true }) : target,
  mod
));

// node_modules/react/cjs/react.production.js
var require_react_production = __commonJS({
  "node_modules/react/cjs/react.production.js"(exports2) {
    "use strict";
    var REACT_ELEMENT_TYPE = /* @__PURE__ */ Symbol.for("react.transitional.element");
    var REACT_PORTAL_TYPE = /* @__PURE__ */ Symbol.for("react.portal");
    var REACT_FRAGMENT_TYPE = /* @__PURE__ */ Symbol.for("react.fragment");
    var REACT_STRICT_MODE_TYPE = /* @__PURE__ */ Symbol.for("react.strict_mode");
    var REACT_PROFILER_TYPE = /* @__PURE__ */ Symbol.for("react.profiler");
    var REACT_CONSUMER_TYPE = /* @__PURE__ */ Symbol.for("react.consumer");
    var REACT_CONTEXT_TYPE = /* @__PURE__ */ Symbol.for("react.context");
    var REACT_FORWARD_REF_TYPE = /* @__PURE__ */ Symbol.for("react.forward_ref");
    var REACT_SUSPENSE_TYPE = /* @__PURE__ */ Symbol.for("react.suspense");
    var REACT_MEMO_TYPE = /* @__PURE__ */ Symbol.for("react.memo");
    var REACT_LAZY_TYPE = /* @__PURE__ */ Symbol.for("react.lazy");
    var REACT_ACTIVITY_TYPE = /* @__PURE__ */ Symbol.for("react.activity");
    var REACT_VIEW_TRANSITION_TYPE = /* @__PURE__ */ Symbol.for("react.view_transition");
    var MAYBE_ITERATOR_SYMBOL = Symbol.iterator;
    function getIteratorFn(maybeIterable) {
      if (null === maybeIterable || "object" !== typeof maybeIterable) return null;
      maybeIterable = MAYBE_ITERATOR_SYMBOL && maybeIterable[MAYBE_ITERATOR_SYMBOL] || maybeIterable["@@iterator"];
      return "function" === typeof maybeIterable ? maybeIterable : null;
    }
    var ReactNoopUpdateQueue = {
      isMounted: function() {
        return false;
      },
      enqueueForceUpdate: function() {
      },
      enqueueReplaceState: function() {
      },
      enqueueSetState: function() {
      }
    };
    var assign = Object.assign;
    var emptyObject = {};
    function Component(props, context, updater) {
      this.props = props;
      this.context = context;
      this.refs = emptyObject;
      this.updater = updater || ReactNoopUpdateQueue;
    }
    Component.prototype.isReactComponent = {};
    Component.prototype.setState = function(partialState, callback) {
      if ("object" !== typeof partialState && "function" !== typeof partialState && null != partialState)
        throw Error(
          "takes an object of state variables to update or a function which returns an object of state variables."
        );
      this.updater.enqueueSetState(this, partialState, callback, "setState");
    };
    Component.prototype.forceUpdate = function(callback) {
      this.updater.enqueueForceUpdate(this, callback, "forceUpdate");
    };
    function ComponentDummy() {
    }
    ComponentDummy.prototype = Component.prototype;
    function PureComponent(props, context, updater) {
      this.props = props;
      this.context = context;
      this.refs = emptyObject;
      this.updater = updater || ReactNoopUpdateQueue;
    }
    var pureComponentPrototype = PureComponent.prototype = new ComponentDummy();
    pureComponentPrototype.constructor = PureComponent;
    assign(pureComponentPrototype, Component.prototype);
    pureComponentPrototype.isPureReactComponent = true;
    var isArrayImpl = Array.isArray;
    function noop() {
    }
    var ReactSharedInternals = { H: null, A: null, T: null, S: null };
    var hasOwnProperty = Object.prototype.hasOwnProperty;
    function ReactElement(type, key, props) {
      var refProp = props.ref;
      return {
        $$typeof: REACT_ELEMENT_TYPE,
        type,
        key,
        ref: void 0 !== refProp ? refProp : null,
        props
      };
    }
    function cloneAndReplaceKey(oldElement, newKey) {
      return ReactElement(oldElement.type, newKey, oldElement.props);
    }
    function isValidElement(object) {
      return "object" === typeof object && null !== object && object.$$typeof === REACT_ELEMENT_TYPE;
    }
    function escape(key) {
      var escaperLookup = { "=": "=0", ":": "=2" };
      return "$" + key.replace(/[=:]/g, function(match) {
        return escaperLookup[match];
      });
    }
    var userProvidedKeyEscapeRegex = /\/+/g;
    function getElementKey(element, index) {
      return "object" === typeof element && null !== element && null != element.key ? escape("" + element.key) : index.toString(36);
    }
    function resolveThenable(thenable) {
      switch (thenable.status) {
        case "fulfilled":
          return thenable.value;
        case "rejected":
          throw thenable.reason;
        default:
          switch ("string" === typeof thenable.status ? thenable.then(noop, noop) : (thenable.status = "pending", thenable.then(
            function(fulfilledValue) {
              "pending" === thenable.status && (thenable.status = "fulfilled", thenable.value = fulfilledValue);
            },
            function(error) {
              "pending" === thenable.status && (thenable.status = "rejected", thenable.reason = error);
            }
          )), thenable.status) {
            case "fulfilled":
              return thenable.value;
            case "rejected":
              throw thenable.reason;
          }
      }
      throw thenable;
    }
    function mapIntoArray(children, array, escapedPrefix, nameSoFar, callback) {
      var type = typeof children;
      if ("undefined" === type || "boolean" === type) children = null;
      var invokeCallback = false;
      if (null === children) invokeCallback = true;
      else
        switch (type) {
          case "bigint":
          case "string":
          case "number":
            invokeCallback = true;
            break;
          case "object":
            switch (children.$$typeof) {
              case REACT_ELEMENT_TYPE:
              case REACT_PORTAL_TYPE:
                invokeCallback = true;
                break;
              case REACT_LAZY_TYPE:
                return invokeCallback = children._init, mapIntoArray(
                  invokeCallback(children._payload),
                  array,
                  escapedPrefix,
                  nameSoFar,
                  callback
                );
            }
        }
      if (invokeCallback)
        return callback = callback(children), invokeCallback = "" === nameSoFar ? "." + getElementKey(children, 0) : nameSoFar, isArrayImpl(callback) ? (escapedPrefix = "", null != invokeCallback && (escapedPrefix = invokeCallback.replace(userProvidedKeyEscapeRegex, "$&/") + "/"), mapIntoArray(callback, array, escapedPrefix, "", function(c) {
          return c;
        })) : null != callback && (isValidElement(callback) && (callback = cloneAndReplaceKey(
          callback,
          escapedPrefix + (null == callback.key || children && children.key === callback.key ? "" : ("" + callback.key).replace(
            userProvidedKeyEscapeRegex,
            "$&/"
          ) + "/") + invokeCallback
        )), array.push(callback)), 1;
      invokeCallback = 0;
      var nextNamePrefix = "" === nameSoFar ? "." : nameSoFar + ":";
      if (isArrayImpl(children))
        for (var i = 0; i < children.length; i++)
          nameSoFar = children[i], type = nextNamePrefix + getElementKey(nameSoFar, i), invokeCallback += mapIntoArray(
            nameSoFar,
            array,
            escapedPrefix,
            type,
            callback
          );
      else if (i = getIteratorFn(children), "function" === typeof i)
        for (children = i.call(children), i = 0; !(nameSoFar = children.next()).done; )
          nameSoFar = nameSoFar.value, type = nextNamePrefix + getElementKey(nameSoFar, i++), invokeCallback += mapIntoArray(
            nameSoFar,
            array,
            escapedPrefix,
            type,
            callback
          );
      else if ("object" === type) {
        if ("function" === typeof children.then)
          return mapIntoArray(
            resolveThenable(children),
            array,
            escapedPrefix,
            nameSoFar,
            callback
          );
        array = String(children);
        throw Error(
          "Objects are not valid as a React child (found: " + ("[object Object]" === array ? "object with keys {" + Object.keys(children).join(", ") + "}" : array) + "). If you meant to render a collection of children, use an array instead."
        );
      }
      return invokeCallback;
    }
    function mapChildren(children, func, context) {
      if (null == children) return children;
      var result = [], count = 0;
      mapIntoArray(children, result, "", "", function(child) {
        return func.call(context, child, count++);
      });
      return result;
    }
    function lazyInitializer(payload) {
      if (-1 === payload._status) {
        var ctor = payload._result, thenable = ctor();
        thenable.then(
          function(moduleObject) {
            if (0 === payload._status || -1 === payload._status)
              payload._status = 1, payload._result = moduleObject, void 0 === thenable.status && (thenable.status = "fulfilled", thenable.value = moduleObject);
          },
          function(error) {
            if (0 === payload._status || -1 === payload._status)
              payload._status = 2, payload._result = error, void 0 === thenable.status && (thenable.status = "rejected", thenable.reason = error);
          }
        );
        -1 === payload._status && (payload._status = 0, payload._result = thenable);
      }
      if (1 === payload._status) return payload._result.default;
      throw payload._result;
    }
    var reportGlobalError = "function" === typeof reportError ? reportError : function(error) {
      if ("object" === typeof window && "function" === typeof window.ErrorEvent) {
        var event = new window.ErrorEvent("error", {
          bubbles: true,
          cancelable: true,
          message: "object" === typeof error && null !== error && "string" === typeof error.message ? String(error.message) : String(error),
          error
        });
        if (!window.dispatchEvent(event)) return;
      } else if ("object" === typeof process && "function" === typeof process.emit) {
        process.emit("uncaughtException", error);
        return;
      }
      console.error(error);
    };
    function startTransition(scope) {
      var prevTransition = ReactSharedInternals.T, currentTransition = {};
      currentTransition.types = null !== prevTransition ? prevTransition.types : null;
      ReactSharedInternals.T = currentTransition;
      try {
        var returnValue = scope(), onStartTransitionFinish = ReactSharedInternals.S;
        null !== onStartTransitionFinish && onStartTransitionFinish(currentTransition, returnValue);
        "object" === typeof returnValue && null !== returnValue && "function" === typeof returnValue.then && returnValue.then(noop, reportGlobalError);
      } catch (error) {
        reportGlobalError(error);
      } finally {
        null !== prevTransition && null !== currentTransition.types && (prevTransition.types = currentTransition.types), ReactSharedInternals.T = prevTransition;
      }
    }
    function addTransitionType(type) {
      var transition = ReactSharedInternals.T;
      if (null !== transition) {
        var transitionTypes = transition.types;
        null === transitionTypes ? transition.types = [type] : -1 === transitionTypes.indexOf(type) && transitionTypes.push(type);
      } else startTransition(addTransitionType.bind(null, type));
    }
    var Children = {
      map: mapChildren,
      forEach: function(children, forEachFunc, forEachContext) {
        mapChildren(
          children,
          function() {
            forEachFunc.apply(this, arguments);
          },
          forEachContext
        );
      },
      count: function(children) {
        var n = 0;
        mapChildren(children, function() {
          n++;
        });
        return n;
      },
      toArray: function(children) {
        return mapChildren(children, function(child) {
          return child;
        }) || [];
      },
      only: function(children) {
        if (!isValidElement(children))
          throw Error(
            "React.Children.only expected to receive a single React element child."
          );
        return children;
      }
    };
    exports2.Activity = REACT_ACTIVITY_TYPE;
    exports2.Children = Children;
    exports2.Component = Component;
    exports2.Fragment = REACT_FRAGMENT_TYPE;
    exports2.Profiler = REACT_PROFILER_TYPE;
    exports2.PureComponent = PureComponent;
    exports2.StrictMode = REACT_STRICT_MODE_TYPE;
    exports2.Suspense = REACT_SUSPENSE_TYPE;
    exports2.ViewTransition = REACT_VIEW_TRANSITION_TYPE;
    exports2.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = ReactSharedInternals;
    exports2.__COMPILER_RUNTIME = {
      __proto__: null,
      c: function(size) {
        return ReactSharedInternals.H.useMemoCache(size);
      }
    };
    exports2.addTransitionType = addTransitionType;
    exports2.cache = function(fn) {
      return function() {
        return fn.apply(null, arguments);
      };
    };
    exports2.cacheSignal = function() {
      return null;
    };
    exports2.cloneElement = function(element, config, children) {
      if (null === element || void 0 === element)
        throw Error(
          "The argument must be a React element, but you passed " + element + "."
        );
      var props = assign({}, element.props), key = element.key;
      if (null != config)
        for (propName in void 0 !== config.key && (key = "" + config.key), config)
          !hasOwnProperty.call(config, propName) || "key" === propName || "__self" === propName || "__source" === propName || "ref" === propName && void 0 === config.ref || (props[propName] = config[propName]);
      var propName = arguments.length - 2;
      if (1 === propName) props.children = children;
      else if (1 < propName) {
        for (var childArray = Array(propName), i = 0; i < propName; i++)
          childArray[i] = arguments[i + 2];
        props.children = childArray;
      }
      return ReactElement(element.type, key, props);
    };
    exports2.createContext = function(defaultValue) {
      defaultValue = {
        $$typeof: REACT_CONTEXT_TYPE,
        _currentValue: defaultValue,
        _currentValue2: defaultValue,
        _threadCount: 0,
        Provider: null,
        Consumer: null
      };
      defaultValue.Provider = defaultValue;
      defaultValue.Consumer = {
        $$typeof: REACT_CONSUMER_TYPE,
        _context: defaultValue
      };
      return defaultValue;
    };
    exports2.createElement = function(type, config, children) {
      var propName, props = {}, key = null;
      if (null != config)
        for (propName in void 0 !== config.key && (key = "" + config.key), config)
          hasOwnProperty.call(config, propName) && "key" !== propName && "__self" !== propName && "__source" !== propName && (props[propName] = config[propName]);
      var childrenLength = arguments.length - 2;
      if (1 === childrenLength) props.children = children;
      else if (1 < childrenLength) {
        for (var childArray = Array(childrenLength), i = 0; i < childrenLength; i++)
          childArray[i] = arguments[i + 2];
        props.children = childArray;
      }
      if (type && type.defaultProps)
        for (propName in childrenLength = type.defaultProps, childrenLength)
          void 0 === props[propName] && (props[propName] = childrenLength[propName]);
      return ReactElement(type, key, props);
    };
    exports2.createRef = function() {
      return { current: null };
    };
    exports2.forwardRef = function(render) {
      return { $$typeof: REACT_FORWARD_REF_TYPE, render };
    };
    exports2.isValidElement = isValidElement;
    exports2.lazy = function(ctor) {
      return {
        $$typeof: REACT_LAZY_TYPE,
        _payload: { _status: -1, _result: ctor },
        _init: lazyInitializer
      };
    };
    exports2.memo = function(type, compare) {
      return {
        $$typeof: REACT_MEMO_TYPE,
        type,
        compare: void 0 === compare ? null : compare
      };
    };
    exports2.startTransition = startTransition;
    exports2.unstable_useCacheRefresh = function() {
      return ReactSharedInternals.H.useCacheRefresh();
    };
    exports2.use = function(usable) {
      return ReactSharedInternals.H.use(usable);
    };
    exports2.useActionState = function(action, initialState, permalink) {
      return ReactSharedInternals.H.useActionState(action, initialState, permalink);
    };
    exports2.useCallback = function(callback, deps) {
      return ReactSharedInternals.H.useCallback(callback, deps);
    };
    exports2.useContext = function(Context) {
      return ReactSharedInternals.H.useContext(Context);
    };
    exports2.useDebugValue = function() {
    };
    exports2.useDeferredValue = function(value, initialValue) {
      return ReactSharedInternals.H.useDeferredValue(value, initialValue);
    };
    exports2.useEffect = function(create, deps) {
      return ReactSharedInternals.H.useEffect(create, deps);
    };
    exports2.useEffectEvent = function(callback) {
      return ReactSharedInternals.H.useEffectEvent(callback);
    };
    exports2.useId = function() {
      return ReactSharedInternals.H.useId();
    };
    exports2.useImperativeHandle = function(ref, create, deps) {
      return ReactSharedInternals.H.useImperativeHandle(ref, create, deps);
    };
    exports2.useInsertionEffect = function(create, deps) {
      return ReactSharedInternals.H.useInsertionEffect(create, deps);
    };
    exports2.useLayoutEffect = function(create, deps) {
      return ReactSharedInternals.H.useLayoutEffect(create, deps);
    };
    exports2.useMemo = function(create, deps) {
      return ReactSharedInternals.H.useMemo(create, deps);
    };
    exports2.useOptimistic = function(passthrough, reducer) {
      return ReactSharedInternals.H.useOptimistic(passthrough, reducer);
    };
    exports2.useReducer = function(reducer, initialArg, init) {
      return ReactSharedInternals.H.useReducer(reducer, initialArg, init);
    };
    exports2.useRef = function(initialValue) {
      return ReactSharedInternals.H.useRef(initialValue);
    };
    exports2.useState = function(initialState) {
      return ReactSharedInternals.H.useState(initialState);
    };
    exports2.useSyncExternalStore = function(subscribe, getSnapshot, getServerSnapshot) {
      return ReactSharedInternals.H.useSyncExternalStore(
        subscribe,
        getSnapshot,
        getServerSnapshot
      );
    };
    exports2.useTransition = function() {
      return ReactSharedInternals.H.useTransition();
    };
    exports2.version = "19.3.1";
  }
});

// node_modules/react/cjs/react.development.js
var require_react_development = __commonJS({
  "node_modules/react/cjs/react.development.js"(exports2, module2) {
    "use strict";
    "production" !== process.env.NODE_ENV && (function() {
      function defineDeprecationWarning(methodName, info) {
        Object.defineProperty(Component.prototype, methodName, {
          get: function() {
            console.warn(
              "%s(...) is deprecated in plain JavaScript React classes. %s",
              info[0],
              info[1]
            );
          }
        });
      }
      function getIteratorFn(maybeIterable) {
        if (null === maybeIterable || "object" !== typeof maybeIterable)
          return null;
        maybeIterable = MAYBE_ITERATOR_SYMBOL && maybeIterable[MAYBE_ITERATOR_SYMBOL] || maybeIterable["@@iterator"];
        return "function" === typeof maybeIterable ? maybeIterable : null;
      }
      function warnNoop(publicInstance, callerName) {
        publicInstance = (publicInstance = publicInstance.constructor) && (publicInstance.displayName || publicInstance.name) || "ReactClass";
        var warningKey = publicInstance + "." + callerName;
        didWarnStateUpdateForUnmountedComponent[warningKey] || (console.error(
          "Can't call %s on a component that is not yet mounted. This is a no-op, but it might indicate a bug in your application. Instead, assign to `this.state` directly or define a `state = {};` class property with the desired state in the %s component.",
          callerName,
          publicInstance
        ), didWarnStateUpdateForUnmountedComponent[warningKey] = true);
      }
      function Component(props, context, updater) {
        this.props = props;
        this.context = context;
        this.refs = emptyObject;
        this.updater = updater || ReactNoopUpdateQueue;
      }
      function ComponentDummy() {
      }
      function PureComponent(props, context, updater) {
        this.props = props;
        this.context = context;
        this.refs = emptyObject;
        this.updater = updater || ReactNoopUpdateQueue;
      }
      function noop() {
      }
      function testStringCoercion(value) {
        return "" + value;
      }
      function checkKeyStringCoercion(value) {
        try {
          testStringCoercion(value);
          var JSCompiler_inline_result = false;
        } catch (e) {
          JSCompiler_inline_result = true;
        }
        if (JSCompiler_inline_result) {
          JSCompiler_inline_result = console;
          var JSCompiler_temp_const = JSCompiler_inline_result.error;
          var JSCompiler_inline_result$jscomp$0 = "function" === typeof Symbol && Symbol.toStringTag && value[Symbol.toStringTag] || value.constructor.name || "Object";
          JSCompiler_temp_const.call(
            JSCompiler_inline_result,
            "The provided key is an unsupported type %s. This value must be coerced to a string before using it here.",
            JSCompiler_inline_result$jscomp$0
          );
          return testStringCoercion(value);
        }
      }
      function getComponentNameFromType(type) {
        if (null == type) return null;
        if ("function" === typeof type)
          return type.$$typeof === REACT_CLIENT_REFERENCE ? null : type.displayName || type.name || null;
        if ("string" === typeof type) return type;
        switch (type) {
          case REACT_FRAGMENT_TYPE:
            return "Fragment";
          case REACT_PROFILER_TYPE:
            return "Profiler";
          case REACT_STRICT_MODE_TYPE:
            return "StrictMode";
          case REACT_SUSPENSE_TYPE:
            return "Suspense";
          case REACT_SUSPENSE_LIST_TYPE:
            return "SuspenseList";
          case REACT_ACTIVITY_TYPE:
            return "Activity";
          case REACT_VIEW_TRANSITION_TYPE:
            return "ViewTransition";
        }
        if ("object" === typeof type)
          switch ("number" === typeof type.tag && console.error(
            "Received an unexpected object in getComponentNameFromType(). This is likely a bug in React. Please file an issue."
          ), type.$$typeof) {
            case REACT_PORTAL_TYPE:
              return "Portal";
            case REACT_CONTEXT_TYPE:
              return type.displayName || "Context";
            case REACT_CONSUMER_TYPE:
              return (type._context.displayName || "Context") + ".Consumer";
            case REACT_FORWARD_REF_TYPE:
              var innerType = type.render;
              type = type.displayName;
              type || (type = innerType.displayName || innerType.name || "", type = "" !== type ? "ForwardRef(" + type + ")" : "ForwardRef");
              return type;
            case REACT_MEMO_TYPE:
              return innerType = type.displayName || null, null !== innerType ? innerType : getComponentNameFromType(type.type) || "Memo";
            case REACT_LAZY_TYPE:
              innerType = type._payload;
              type = type._init;
              try {
                return getComponentNameFromType(type(innerType));
              } catch (x) {
              }
          }
        return null;
      }
      function getTaskName(type) {
        if (type === REACT_FRAGMENT_TYPE) return "<>";
        if ("object" === typeof type && null !== type && type.$$typeof === REACT_LAZY_TYPE)
          return "<...>";
        try {
          var name = getComponentNameFromType(type);
          return name ? "<" + name + ">" : "<...>";
        } catch (x) {
          return "<...>";
        }
      }
      function getOwner() {
        var dispatcher = ReactSharedInternals.A;
        return null === dispatcher ? null : dispatcher.getOwner();
      }
      function UnknownOwner() {
        return Error("react-stack-top-frame");
      }
      function hasValidKey(config) {
        if (hasOwnProperty.call(config, "key")) {
          var getter = Object.getOwnPropertyDescriptor(config, "key").get;
          if (getter && getter.isReactWarning) return false;
        }
        return void 0 !== config.key;
      }
      function defineKeyPropWarningGetter(props, displayName) {
        function warnAboutAccessingKey() {
          specialPropKeyWarningShown || (specialPropKeyWarningShown = true, console.error(
            "%s: `key` is not a prop. Trying to access it will result in `undefined` being returned. If you need to access the same value within the child component, you should pass it as a different prop. (https://react.dev/link/special-props)",
            displayName
          ));
        }
        warnAboutAccessingKey.isReactWarning = true;
        Object.defineProperty(props, "key", {
          get: warnAboutAccessingKey,
          configurable: true
        });
      }
      function elementRefGetterWithDeprecationWarning() {
        var componentName = getComponentNameFromType(this.type);
        didWarnAboutElementRef[componentName] || (didWarnAboutElementRef[componentName] = true, console.error(
          "Accessing element.ref was removed in React 19. ref is now a regular prop. It will be removed from the JSX Element type in a future release."
        ));
        componentName = this.props.ref;
        return void 0 !== componentName ? componentName : null;
      }
      function ReactElement(type, key, props, owner, debugStack, debugTask) {
        var refProp = props.ref;
        type = {
          $$typeof: REACT_ELEMENT_TYPE,
          type,
          key,
          props,
          _owner: owner
        };
        null !== (void 0 !== refProp ? refProp : null) ? Object.defineProperty(type, "ref", {
          enumerable: false,
          get: elementRefGetterWithDeprecationWarning
        }) : Object.defineProperty(type, "ref", { enumerable: false, value: null });
        type._store = {};
        Object.defineProperty(type._store, "validated", {
          configurable: false,
          enumerable: false,
          writable: true,
          value: 0
        });
        Object.defineProperty(type, "_debugInfo", {
          configurable: false,
          enumerable: false,
          writable: true,
          value: null
        });
        Object.defineProperty(type, "_debugStack", {
          configurable: false,
          enumerable: false,
          writable: true,
          value: debugStack
        });
        Object.defineProperty(type, "_debugTask", {
          configurable: false,
          enumerable: false,
          writable: true,
          value: debugTask
        });
        Object.freeze && (Object.freeze(type.props), Object.freeze(type));
        return type;
      }
      function cloneAndReplaceKey(oldElement, newKey) {
        newKey = ReactElement(
          oldElement.type,
          newKey,
          oldElement.props,
          oldElement._owner,
          oldElement._debugStack,
          oldElement._debugTask
        );
        oldElement._store && (newKey._store.validated = oldElement._store.validated);
        return newKey;
      }
      function validateChildKeys(node) {
        isValidElement(node) ? node._store && (node._store.validated = 1) : "object" === typeof node && null !== node && node.$$typeof === REACT_LAZY_TYPE && ("fulfilled" === node._payload.status ? isValidElement(node._payload.value) && node._payload.value._store && (node._payload.value._store.validated = 1) : node._store && (node._store.validated = 1));
      }
      function isValidElement(object) {
        return "object" === typeof object && null !== object && object.$$typeof === REACT_ELEMENT_TYPE;
      }
      function escape(key) {
        var escaperLookup = { "=": "=0", ":": "=2" };
        return "$" + key.replace(/[=:]/g, function(match) {
          return escaperLookup[match];
        });
      }
      function getElementKey(element, index) {
        return "object" === typeof element && null !== element && null != element.key ? (checkKeyStringCoercion(element.key), escape("" + element.key)) : index.toString(36);
      }
      function resolveThenable(thenable) {
        switch (thenable.status) {
          case "fulfilled":
            return thenable.value;
          case "rejected":
            throw thenable.reason;
          default:
            switch ("string" === typeof thenable.status ? thenable.then(noop, noop) : (thenable.status = "pending", thenable.then(
              function(fulfilledValue) {
                "pending" === thenable.status && (thenable.status = "fulfilled", thenable.value = fulfilledValue);
              },
              function(error) {
                "pending" === thenable.status && (thenable.status = "rejected", thenable.reason = error);
              }
            )), thenable.status) {
              case "fulfilled":
                return thenable.value;
              case "rejected":
                throw thenable.reason;
            }
        }
        throw thenable;
      }
      function mapIntoArray(children, array, escapedPrefix, nameSoFar, callback) {
        var type = typeof children;
        if ("undefined" === type || "boolean" === type) children = null;
        var invokeCallback = false;
        if (null === children) invokeCallback = true;
        else
          switch (type) {
            case "bigint":
            case "string":
            case "number":
              invokeCallback = true;
              break;
            case "object":
              switch (children.$$typeof) {
                case REACT_ELEMENT_TYPE:
                case REACT_PORTAL_TYPE:
                  invokeCallback = true;
                  break;
                case REACT_LAZY_TYPE:
                  return invokeCallback = children._init, mapIntoArray(
                    invokeCallback(children._payload),
                    array,
                    escapedPrefix,
                    nameSoFar,
                    callback
                  );
              }
          }
        if (invokeCallback) {
          invokeCallback = children;
          callback = callback(invokeCallback);
          var childKey = "" === nameSoFar ? "." + getElementKey(invokeCallback, 0) : nameSoFar;
          isArrayImpl(callback) ? (escapedPrefix = "", null != childKey && (escapedPrefix = childKey.replace(userProvidedKeyEscapeRegex, "$&/") + "/"), mapIntoArray(callback, array, escapedPrefix, "", function(c) {
            return c;
          })) : null != callback && (isValidElement(callback) && (null != callback.key && (invokeCallback && invokeCallback.key === callback.key || checkKeyStringCoercion(callback.key)), escapedPrefix = cloneAndReplaceKey(
            callback,
            escapedPrefix + (null == callback.key || invokeCallback && invokeCallback.key === callback.key ? "" : ("" + callback.key).replace(
              userProvidedKeyEscapeRegex,
              "$&/"
            ) + "/") + childKey
          ), "" !== nameSoFar && null != invokeCallback && isValidElement(invokeCallback) && null == invokeCallback.key && invokeCallback._store && !invokeCallback._store.validated && (escapedPrefix._store.validated = 2), callback = escapedPrefix), array.push(callback));
          return 1;
        }
        invokeCallback = 0;
        childKey = "" === nameSoFar ? "." : nameSoFar + ":";
        if (isArrayImpl(children))
          for (var i = 0; i < children.length; i++)
            nameSoFar = children[i], type = childKey + getElementKey(nameSoFar, i), invokeCallback += mapIntoArray(
              nameSoFar,
              array,
              escapedPrefix,
              type,
              callback
            );
        else if (i = getIteratorFn(children), "function" === typeof i)
          for (i === children.entries && (didWarnAboutMaps || console.warn(
            "Using Maps as children is not supported. Use an array of keyed ReactElements instead."
          ), didWarnAboutMaps = true), children = i.call(children), i = 0; !(nameSoFar = children.next()).done; )
            nameSoFar = nameSoFar.value, type = childKey + getElementKey(nameSoFar, i++), invokeCallback += mapIntoArray(
              nameSoFar,
              array,
              escapedPrefix,
              type,
              callback
            );
        else if ("object" === type) {
          if ("function" === typeof children.then)
            return mapIntoArray(
              resolveThenable(children),
              array,
              escapedPrefix,
              nameSoFar,
              callback
            );
          array = String(children);
          throw Error(
            "Objects are not valid as a React child (found: " + ("[object Object]" === array ? "object with keys {" + Object.keys(children).join(", ") + "}" : array) + "). If you meant to render a collection of children, use an array instead."
          );
        }
        return invokeCallback;
      }
      function mapChildren(children, func, context) {
        if (null == children) return children;
        var result = [], count = 0;
        mapIntoArray(children, result, "", "", function(child) {
          return func.call(context, child, count++);
        });
        return result;
      }
      function lazyInitializer(payload) {
        if (-1 === payload._status) {
          var resolveDebugValue = null, rejectDebugValue = null, ioInfo = payload._ioInfo;
          null != ioInfo && (ioInfo.start = ioInfo.end = performance.now(), ioInfo.value = new Promise(function(resolve, reject) {
            resolveDebugValue = resolve;
            rejectDebugValue = reject;
          }));
          ioInfo = payload._result;
          var thenable = ioInfo();
          thenable.then(
            function(moduleObject) {
              if (0 === payload._status || -1 === payload._status) {
                payload._status = 1;
                payload._result = moduleObject;
                var _ioInfo = payload._ioInfo;
                if (null != _ioInfo) {
                  _ioInfo.end = performance.now();
                  var debugValue = null == moduleObject ? void 0 : moduleObject.default;
                  resolveDebugValue(debugValue);
                  _ioInfo.value.status = "fulfilled";
                  _ioInfo.value.value = debugValue;
                }
                void 0 === thenable.status && (thenable.status = "fulfilled", thenable.value = moduleObject);
              }
            },
            function(error) {
              if (0 === payload._status || -1 === payload._status) {
                payload._status = 2;
                payload._result = error;
                var _ioInfo2 = payload._ioInfo;
                null != _ioInfo2 && (_ioInfo2.end = performance.now(), _ioInfo2.value.then(noop, noop), rejectDebugValue(error), _ioInfo2.value.status = "rejected", _ioInfo2.value.reason = error);
                void 0 === thenable.status && (thenable.status = "rejected", thenable.reason = error);
              }
            }
          );
          ioInfo = payload._ioInfo;
          if (null != ioInfo) {
            var displayName = thenable.displayName;
            "string" === typeof displayName && (ioInfo.name = displayName);
          }
          -1 === payload._status && (payload._status = 0, payload._result = thenable);
        }
        if (1 === payload._status)
          return ioInfo = payload._result, void 0 === ioInfo && console.error(
            "lazy: Expected the result of a dynamic import() call. Instead received: %s\n\nYour code should look like: \n  const MyComponent = lazy(() => import('./MyComponent'))\n\nDid you accidentally put curly braces around the import?",
            ioInfo
          ), "default" in ioInfo || console.error(
            "lazy: Expected the result of a dynamic import() call. Instead received: %s\n\nYour code should look like: \n  const MyComponent = lazy(() => import('./MyComponent'))",
            ioInfo
          ), ioInfo.default;
        throw payload._result;
      }
      function resolveDispatcher() {
        var dispatcher = ReactSharedInternals.H;
        null === dispatcher && console.error(
          "Invalid hook call. Hooks can only be called inside of the body of a function component. This could happen for one of the following reasons:\n1. You might have mismatching versions of React and the renderer (such as React DOM)\n2. You might be breaking the Rules of Hooks\n3. You might have more than one copy of React in the same app\nSee https://react.dev/link/invalid-hook-call for tips about how to debug and fix this problem."
        );
        return dispatcher;
      }
      function releaseAsyncTransition() {
        ReactSharedInternals.asyncTransitions--;
      }
      function startTransition(scope) {
        var prevTransition = ReactSharedInternals.T, currentTransition = {};
        currentTransition.types = null !== prevTransition ? prevTransition.types : null;
        currentTransition._updatedFibers = /* @__PURE__ */ new Set();
        ReactSharedInternals.T = currentTransition;
        try {
          var returnValue = scope(), onStartTransitionFinish = ReactSharedInternals.S;
          null !== onStartTransitionFinish && onStartTransitionFinish(currentTransition, returnValue);
          "object" === typeof returnValue && null !== returnValue && "function" === typeof returnValue.then && (ReactSharedInternals.asyncTransitions++, returnValue.then(releaseAsyncTransition, releaseAsyncTransition), returnValue.then(noop, reportGlobalError));
        } catch (error) {
          reportGlobalError(error);
        } finally {
          null === prevTransition && currentTransition._updatedFibers && (scope = currentTransition._updatedFibers.size, currentTransition._updatedFibers.clear(), 10 < scope && console.warn(
            "Detected a large number of updates inside startTransition. If this is due to a subscription please re-write it to use React provided hooks. Otherwise concurrent mode guarantees are off the table."
          )), null !== prevTransition && null !== currentTransition.types && (null !== prevTransition.types && prevTransition.types !== currentTransition.types && console.error(
            "We expected inner Transitions to have transferred the outer types set and that you cannot add to the outer Transition while inside the inner.This is a bug in React."
          ), prevTransition.types = currentTransition.types), ReactSharedInternals.T = prevTransition;
        }
      }
      function addTransitionType(type) {
        var transition = ReactSharedInternals.T;
        if (null !== transition) {
          var transitionTypes = transition.types;
          null === transitionTypes ? transition.types = [type] : -1 === transitionTypes.indexOf(type) && transitionTypes.push(type);
        } else
          0 === ReactSharedInternals.asyncTransitions && console.error(
            "addTransitionType can only be called inside a `startTransition()` callback. It must be associated with a specific Transition."
          ), startTransition(addTransitionType.bind(null, type));
      }
      function enqueueTask(task) {
        if (null === enqueueTaskImpl)
          try {
            var requireString = ("require" + Math.random()).slice(0, 7);
            enqueueTaskImpl = (module2 && module2[requireString]).call(
              module2,
              "timers"
            ).setImmediate;
          } catch (_err) {
            enqueueTaskImpl = function(callback) {
              false === didWarnAboutMessageChannel && (didWarnAboutMessageChannel = true, "undefined" === typeof MessageChannel && console.error(
                "This browser does not have a MessageChannel implementation, so enqueuing tasks via await act(async () => ...) will fail. Please file an issue at https://github.com/facebook/react/issues if you encounter this warning."
              ));
              var channel = new MessageChannel();
              channel.port1.onmessage = callback;
              channel.port2.postMessage(void 0);
            };
          }
        return enqueueTaskImpl(task);
      }
      function aggregateErrors(errors) {
        return 1 < errors.length && "function" === typeof AggregateError ? new AggregateError(errors) : errors[0];
      }
      function popActScope(prevActQueue, prevActScopeDepth) {
        prevActScopeDepth !== actScopeDepth - 1 && console.error(
          "You seem to have overlapping act() calls, this is not supported. Be sure to await previous act() calls before making a new one. "
        );
        actScopeDepth = prevActScopeDepth;
      }
      function recursivelyFlushAsyncActWork(returnValue, resolve, reject) {
        var queue = ReactSharedInternals.actQueue;
        if (null !== queue)
          if (0 !== queue.length)
            try {
              flushActQueue(queue);
              enqueueTask(function() {
                return recursivelyFlushAsyncActWork(returnValue, resolve, reject);
              });
              return;
            } catch (error) {
              ReactSharedInternals.thrownErrors.push(error);
            }
          else ReactSharedInternals.actQueue = null;
        0 < ReactSharedInternals.thrownErrors.length ? (queue = aggregateErrors(ReactSharedInternals.thrownErrors), ReactSharedInternals.thrownErrors.length = 0, reject(queue)) : resolve(returnValue);
      }
      function flushActQueue(queue) {
        if (!isFlushing) {
          isFlushing = true;
          var i = 0;
          try {
            for (; i < queue.length; i++) {
              var callback = queue[i];
              do {
                ReactSharedInternals.didUsePromise = false;
                var continuation = callback(false);
                if (null !== continuation) {
                  if (ReactSharedInternals.didUsePromise) {
                    queue[i] = callback;
                    queue.splice(0, i);
                    return;
                  }
                  callback = continuation;
                } else break;
              } while (1);
            }
            queue.length = 0;
          } catch (error) {
            queue.splice(0, i + 1), ReactSharedInternals.thrownErrors.push(error);
          } finally {
            isFlushing = false;
          }
        }
      }
      "undefined" !== typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ && "function" === typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.registerInternalModuleStart && __REACT_DEVTOOLS_GLOBAL_HOOK__.registerInternalModuleStart(Error());
      var REACT_ELEMENT_TYPE = /* @__PURE__ */ Symbol.for("react.transitional.element"), REACT_PORTAL_TYPE = /* @__PURE__ */ Symbol.for("react.portal"), REACT_FRAGMENT_TYPE = /* @__PURE__ */ Symbol.for("react.fragment"), REACT_STRICT_MODE_TYPE = /* @__PURE__ */ Symbol.for("react.strict_mode"), REACT_PROFILER_TYPE = /* @__PURE__ */ Symbol.for("react.profiler"), REACT_CONSUMER_TYPE = /* @__PURE__ */ Symbol.for("react.consumer"), REACT_CONTEXT_TYPE = /* @__PURE__ */ Symbol.for("react.context"), REACT_FORWARD_REF_TYPE = /* @__PURE__ */ Symbol.for("react.forward_ref"), REACT_SUSPENSE_TYPE = /* @__PURE__ */ Symbol.for("react.suspense"), REACT_SUSPENSE_LIST_TYPE = /* @__PURE__ */ Symbol.for("react.suspense_list"), REACT_MEMO_TYPE = /* @__PURE__ */ Symbol.for("react.memo"), REACT_LAZY_TYPE = /* @__PURE__ */ Symbol.for("react.lazy"), REACT_ACTIVITY_TYPE = /* @__PURE__ */ Symbol.for("react.activity"), REACT_VIEW_TRANSITION_TYPE = /* @__PURE__ */ Symbol.for("react.view_transition"), MAYBE_ITERATOR_SYMBOL = Symbol.iterator, didWarnStateUpdateForUnmountedComponent = {}, ReactNoopUpdateQueue = {
        isMounted: function() {
          return false;
        },
        enqueueForceUpdate: function(publicInstance) {
          warnNoop(publicInstance, "forceUpdate");
        },
        enqueueReplaceState: function(publicInstance) {
          warnNoop(publicInstance, "replaceState");
        },
        enqueueSetState: function(publicInstance) {
          warnNoop(publicInstance, "setState");
        }
      }, assign = Object.assign, emptyObject = {};
      Object.freeze(emptyObject);
      Component.prototype.isReactComponent = {};
      Component.prototype.setState = function(partialState, callback) {
        if ("object" !== typeof partialState && "function" !== typeof partialState && null != partialState)
          throw Error(
            "takes an object of state variables to update or a function which returns an object of state variables."
          );
        this.updater.enqueueSetState(this, partialState, callback, "setState");
      };
      Component.prototype.forceUpdate = function(callback) {
        this.updater.enqueueForceUpdate(this, callback, "forceUpdate");
      };
      var deprecatedAPIs = {
        isMounted: [
          "isMounted",
          "Instead, make sure to clean up subscriptions and pending requests in componentWillUnmount to prevent memory leaks."
        ],
        replaceState: [
          "replaceState",
          "Refactor your code to use setState instead (see https://github.com/facebook/react/issues/3236)."
        ]
      };
      for (fnName in deprecatedAPIs)
        deprecatedAPIs.hasOwnProperty(fnName) && defineDeprecationWarning(fnName, deprecatedAPIs[fnName]);
      ComponentDummy.prototype = Component.prototype;
      deprecatedAPIs = PureComponent.prototype = new ComponentDummy();
      deprecatedAPIs.constructor = PureComponent;
      assign(deprecatedAPIs, Component.prototype);
      deprecatedAPIs.isPureReactComponent = true;
      var isArrayImpl = Array.isArray, REACT_CLIENT_REFERENCE = /* @__PURE__ */ Symbol.for("react.client.reference"), ReactSharedInternals = {
        H: null,
        A: null,
        T: null,
        S: null,
        actQueue: null,
        asyncTransitions: 0,
        isBatchingLegacy: false,
        didScheduleLegacyUpdate: false,
        didUsePromise: false,
        thrownErrors: [],
        getCurrentStack: null,
        recentlyCreatedOwnerStacks: 0
      }, hasOwnProperty = Object.prototype.hasOwnProperty, createTask = console.createTask ? console.createTask : function() {
        return null;
      };
      deprecatedAPIs = {
        react_stack_bottom_frame: function(callStackForError) {
          return callStackForError();
        }
      };
      var specialPropKeyWarningShown, didWarnAboutOldJSXRuntime;
      var didWarnAboutElementRef = {};
      var unknownOwnerDebugStack = deprecatedAPIs.react_stack_bottom_frame.bind(
        deprecatedAPIs,
        UnknownOwner
      )();
      var unknownOwnerDebugTask = createTask(getTaskName(UnknownOwner));
      var didWarnAboutMaps = false, userProvidedKeyEscapeRegex = /\/+/g, reportGlobalError = "function" === typeof reportError ? reportError : function(error) {
        if ("object" === typeof window && "function" === typeof window.ErrorEvent) {
          var event = new window.ErrorEvent("error", {
            bubbles: true,
            cancelable: true,
            message: "object" === typeof error && null !== error && "string" === typeof error.message ? String(error.message) : String(error),
            error
          });
          if (!window.dispatchEvent(event)) return;
        } else if ("object" === typeof process && "function" === typeof process.emit) {
          process.emit("uncaughtException", error);
          return;
        }
        console.error(error);
      }, didWarnAboutMessageChannel = false, enqueueTaskImpl = null, actScopeDepth = 0, didWarnNoAwaitAct = false, isFlushing = false, queueSeveralMicrotasks = "function" === typeof queueMicrotask ? function(callback) {
        queueMicrotask(function() {
          return queueMicrotask(callback);
        });
      } : enqueueTask;
      deprecatedAPIs = Object.freeze({
        __proto__: null,
        c: function(size) {
          return resolveDispatcher().useMemoCache(size);
        }
      });
      var fnName = {
        map: mapChildren,
        forEach: function(children, forEachFunc, forEachContext) {
          mapChildren(
            children,
            function() {
              forEachFunc.apply(this, arguments);
            },
            forEachContext
          );
        },
        count: function(children) {
          var n = 0;
          mapChildren(children, function() {
            n++;
          });
          return n;
        },
        toArray: function(children) {
          return mapChildren(children, function(child) {
            return child;
          }) || [];
        },
        only: function(children) {
          if (!isValidElement(children))
            throw Error(
              "React.Children.only expected to receive a single React element child."
            );
          return children;
        }
      };
      exports2.Activity = REACT_ACTIVITY_TYPE;
      exports2.Children = fnName;
      exports2.Component = Component;
      exports2.Fragment = REACT_FRAGMENT_TYPE;
      exports2.Profiler = REACT_PROFILER_TYPE;
      exports2.PureComponent = PureComponent;
      exports2.StrictMode = REACT_STRICT_MODE_TYPE;
      exports2.Suspense = REACT_SUSPENSE_TYPE;
      exports2.ViewTransition = REACT_VIEW_TRANSITION_TYPE;
      exports2.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = ReactSharedInternals;
      exports2.__COMPILER_RUNTIME = deprecatedAPIs;
      exports2.act = function(callback) {
        var prevActQueue = ReactSharedInternals.actQueue, prevActScopeDepth = actScopeDepth;
        actScopeDepth++;
        var queue = ReactSharedInternals.actQueue = null !== prevActQueue ? prevActQueue : [], didAwaitActCall = false;
        try {
          var result = callback();
        } catch (error) {
          ReactSharedInternals.thrownErrors.push(error);
        }
        if (0 < ReactSharedInternals.thrownErrors.length)
          throw popActScope(prevActQueue, prevActScopeDepth), callback = aggregateErrors(ReactSharedInternals.thrownErrors), ReactSharedInternals.thrownErrors.length = 0, callback;
        if (null !== result && "object" === typeof result && "function" === typeof result.then) {
          var thenable = result;
          queueSeveralMicrotasks(function() {
            didAwaitActCall || didWarnNoAwaitAct || (didWarnNoAwaitAct = true, console.error(
              "You called act(async () => ...) without await. This could lead to unexpected testing behaviour, interleaving multiple act calls and mixing their scopes. You should - await act(async () => ...);"
            ));
          });
          return {
            then: function(resolve, reject) {
              didAwaitActCall = true;
              thenable.then(
                function(returnValue) {
                  popActScope(prevActQueue, prevActScopeDepth);
                  if (0 === prevActScopeDepth) {
                    try {
                      flushActQueue(queue), enqueueTask(function() {
                        return recursivelyFlushAsyncActWork(
                          returnValue,
                          resolve,
                          reject
                        );
                      });
                    } catch (error$0) {
                      ReactSharedInternals.thrownErrors.push(error$0);
                    }
                    if (0 < ReactSharedInternals.thrownErrors.length) {
                      var _thrownError = aggregateErrors(
                        ReactSharedInternals.thrownErrors
                      );
                      ReactSharedInternals.thrownErrors.length = 0;
                      reject(_thrownError);
                    }
                  } else resolve(returnValue);
                },
                function(error) {
                  popActScope(prevActQueue, prevActScopeDepth);
                  0 < ReactSharedInternals.thrownErrors.length ? (error = aggregateErrors(
                    ReactSharedInternals.thrownErrors
                  ), ReactSharedInternals.thrownErrors.length = 0, reject(error)) : reject(error);
                }
              );
            }
          };
        }
        var returnValue$jscomp$0 = result;
        popActScope(prevActQueue, prevActScopeDepth);
        0 === prevActScopeDepth && (flushActQueue(queue), 0 !== queue.length && queueSeveralMicrotasks(function() {
          didAwaitActCall || didWarnNoAwaitAct || (didWarnNoAwaitAct = true, console.error(
            "A component suspended inside an `act` scope, but the `act` call was not awaited. When testing React components that depend on asynchronous data, you must await the result:\n\nawait act(() => ...)"
          ));
        }), ReactSharedInternals.actQueue = null);
        if (0 < ReactSharedInternals.thrownErrors.length)
          throw callback = aggregateErrors(ReactSharedInternals.thrownErrors), ReactSharedInternals.thrownErrors.length = 0, callback;
        return {
          then: function(resolve, reject) {
            didAwaitActCall = true;
            0 === prevActScopeDepth ? (ReactSharedInternals.actQueue = queue, enqueueTask(function() {
              return recursivelyFlushAsyncActWork(
                returnValue$jscomp$0,
                resolve,
                reject
              );
            })) : resolve(returnValue$jscomp$0);
          }
        };
      };
      exports2.addTransitionType = addTransitionType;
      exports2.cache = function(fn) {
        return function() {
          return fn.apply(null, arguments);
        };
      };
      exports2.cacheSignal = function() {
        return null;
      };
      exports2.captureOwnerStack = function() {
        var getCurrentStack = ReactSharedInternals.getCurrentStack;
        return null === getCurrentStack ? null : getCurrentStack();
      };
      exports2.cloneElement = function(element, config, children) {
        if (null === element || void 0 === element)
          throw Error(
            "The argument must be a React element, but you passed " + element + "."
          );
        var props = assign({}, element.props), key = element.key, owner = element._owner;
        if (null != config) {
          var JSCompiler_inline_result;
          a: {
            if (hasOwnProperty.call(config, "ref") && (JSCompiler_inline_result = Object.getOwnPropertyDescriptor(
              config,
              "ref"
            ).get) && JSCompiler_inline_result.isReactWarning) {
              JSCompiler_inline_result = false;
              break a;
            }
            JSCompiler_inline_result = void 0 !== config.ref;
          }
          JSCompiler_inline_result && (owner = getOwner());
          hasValidKey(config) && (checkKeyStringCoercion(config.key), key = "" + config.key);
          for (propName in config)
            !hasOwnProperty.call(config, propName) || "key" === propName || "__self" === propName || "__source" === propName || "ref" === propName && void 0 === config.ref || (props[propName] = config[propName]);
        }
        var propName = arguments.length - 2;
        if (1 === propName) props.children = children;
        else if (1 < propName) {
          JSCompiler_inline_result = Array(propName);
          for (var i = 0; i < propName; i++)
            JSCompiler_inline_result[i] = arguments[i + 2];
          props.children = JSCompiler_inline_result;
        }
        props = ReactElement(
          element.type,
          key,
          props,
          owner,
          element._debugStack,
          element._debugTask
        );
        for (key = 2; key < arguments.length; key++)
          validateChildKeys(arguments[key]);
        return props;
      };
      exports2.createContext = function(defaultValue) {
        defaultValue = {
          $$typeof: REACT_CONTEXT_TYPE,
          _currentValue: defaultValue,
          _currentValue2: defaultValue,
          _threadCount: 0,
          Provider: null,
          Consumer: null
        };
        defaultValue.Provider = defaultValue;
        defaultValue.Consumer = {
          $$typeof: REACT_CONSUMER_TYPE,
          _context: defaultValue
        };
        defaultValue._currentRenderer = null;
        defaultValue._currentRenderer2 = null;
        return defaultValue;
      };
      exports2.createElement = function(type, config, children) {
        for (var i = 2; i < arguments.length; i++)
          validateChildKeys(arguments[i]);
        var propName;
        i = {};
        var key = null;
        if (null != config)
          for (propName in didWarnAboutOldJSXRuntime || !("__self" in config) || "key" in config || (didWarnAboutOldJSXRuntime = true, console.warn(
            "Your app (or one of its dependencies) is using an outdated JSX transform. Update to the modern JSX transform for faster performance: https://react.dev/link/new-jsx-transform"
          )), hasValidKey(config) && (checkKeyStringCoercion(config.key), key = "" + config.key), config)
            hasOwnProperty.call(config, propName) && "key" !== propName && "__self" !== propName && "__source" !== propName && (i[propName] = config[propName]);
        var childrenLength = arguments.length - 2;
        if (1 === childrenLength) i.children = children;
        else if (1 < childrenLength) {
          for (var childArray = Array(childrenLength), _i = 0; _i < childrenLength; _i++)
            childArray[_i] = arguments[_i + 2];
          Object.freeze && Object.freeze(childArray);
          i.children = childArray;
        }
        if (type && type.defaultProps)
          for (propName in childrenLength = type.defaultProps, childrenLength)
            void 0 === i[propName] && (i[propName] = childrenLength[propName]);
        key && defineKeyPropWarningGetter(
          i,
          "function" === typeof type ? type.displayName || type.name || "Unknown" : type
        );
        (propName = 1e4 > ReactSharedInternals.recentlyCreatedOwnerStacks++) ? (childArray = Error.stackTraceLimit, Error.stackTraceLimit = 10, childrenLength = Error("react-stack-top-frame"), Error.stackTraceLimit = childArray) : childrenLength = unknownOwnerDebugStack;
        return ReactElement(
          type,
          key,
          i,
          getOwner(),
          childrenLength,
          propName ? createTask(getTaskName(type)) : unknownOwnerDebugTask
        );
      };
      exports2.createRef = function() {
        var refObject = { current: null };
        Object.seal(refObject);
        return refObject;
      };
      exports2.forwardRef = function(render) {
        null != render && render.$$typeof === REACT_MEMO_TYPE ? console.error(
          "forwardRef requires a render function but received a `memo` component. Instead of forwardRef(memo(...)), use memo(forwardRef(...))."
        ) : "function" !== typeof render ? console.error(
          "forwardRef requires a render function but was given %s.",
          null === render ? "null" : typeof render
        ) : 0 !== render.length && 2 !== render.length && console.error(
          "forwardRef render functions accept exactly two parameters: props and ref. %s",
          1 === render.length ? "Did you forget to use the ref parameter?" : "Any additional parameter will be undefined."
        );
        null != render && null != render.defaultProps && console.error(
          "forwardRef render functions do not support defaultProps. Did you accidentally pass a React component?"
        );
        var elementType = { $$typeof: REACT_FORWARD_REF_TYPE, render }, ownName;
        Object.defineProperty(elementType, "displayName", {
          enumerable: false,
          configurable: true,
          get: function() {
            return ownName;
          },
          set: function(name) {
            ownName = name;
            render.name || render.displayName || (Object.defineProperty(render, "name", { value: name }), render.displayName = name);
          }
        });
        return elementType;
      };
      exports2.isValidElement = isValidElement;
      exports2.lazy = function(ctor) {
        ctor = { _status: -1, _result: ctor };
        var lazyType = {
          $$typeof: REACT_LAZY_TYPE,
          _payload: ctor,
          _init: lazyInitializer
        }, ioInfo = {
          name: "lazy",
          start: -1,
          end: -1,
          value: null,
          owner: null,
          debugStack: Error("react-stack-top-frame"),
          debugTask: console.createTask ? console.createTask("lazy()") : null
        };
        ctor._ioInfo = ioInfo;
        lazyType._debugInfo = [{ awaited: ioInfo }];
        return lazyType;
      };
      exports2.memo = function(type, compare) {
        null == type && console.error(
          "memo: The first argument must be a component. Instead received: %s",
          null === type ? "null" : typeof type
        );
        compare = {
          $$typeof: REACT_MEMO_TYPE,
          type,
          compare: void 0 === compare ? null : compare
        };
        var ownName;
        Object.defineProperty(compare, "displayName", {
          enumerable: false,
          configurable: true,
          get: function() {
            return ownName;
          },
          set: function(name) {
            ownName = name;
            type.name || type.displayName || (Object.defineProperty(type, "name", { value: name }), type.displayName = name);
          }
        });
        return compare;
      };
      exports2.startTransition = startTransition;
      exports2.unstable_useCacheRefresh = function() {
        return resolveDispatcher().useCacheRefresh();
      };
      exports2.use = function(usable) {
        return resolveDispatcher().use(usable);
      };
      exports2.useActionState = function(action, initialState, permalink) {
        return resolveDispatcher().useActionState(
          action,
          initialState,
          permalink
        );
      };
      exports2.useCallback = function(callback, deps) {
        return resolveDispatcher().useCallback(callback, deps);
      };
      exports2.useContext = function(Context) {
        var dispatcher = resolveDispatcher();
        Context.$$typeof === REACT_CONSUMER_TYPE && console.error(
          "Calling useContext(Context.Consumer) is not supported and will cause bugs. Did you mean to call useContext(Context) instead?"
        );
        return dispatcher.useContext(Context);
      };
      exports2.useDebugValue = function(value, formatterFn) {
        return resolveDispatcher().useDebugValue(value, formatterFn);
      };
      exports2.useDeferredValue = function(value, initialValue) {
        return resolveDispatcher().useDeferredValue(value, initialValue);
      };
      exports2.useEffect = function(create, deps) {
        null == create && console.warn(
          "React Hook useEffect requires an effect callback. Did you forget to pass a callback to the hook?"
        );
        return resolveDispatcher().useEffect(create, deps);
      };
      exports2.useEffectEvent = function(callback) {
        return resolveDispatcher().useEffectEvent(callback);
      };
      exports2.useId = function() {
        return resolveDispatcher().useId();
      };
      exports2.useImperativeHandle = function(ref, create, deps) {
        return resolveDispatcher().useImperativeHandle(ref, create, deps);
      };
      exports2.useInsertionEffect = function(create, deps) {
        null == create && console.warn(
          "React Hook useInsertionEffect requires an effect callback. Did you forget to pass a callback to the hook?"
        );
        return resolveDispatcher().useInsertionEffect(create, deps);
      };
      exports2.useLayoutEffect = function(create, deps) {
        null == create && console.warn(
          "React Hook useLayoutEffect requires an effect callback. Did you forget to pass a callback to the hook?"
        );
        return resolveDispatcher().useLayoutEffect(create, deps);
      };
      exports2.useMemo = function(create, deps) {
        return resolveDispatcher().useMemo(create, deps);
      };
      exports2.useOptimistic = function(passthrough, reducer) {
        return resolveDispatcher().useOptimistic(passthrough, reducer);
      };
      exports2.useReducer = function(reducer, initialArg, init) {
        return resolveDispatcher().useReducer(reducer, initialArg, init);
      };
      exports2.useRef = function(initialValue) {
        return resolveDispatcher().useRef(initialValue);
      };
      exports2.useState = function(initialState) {
        return resolveDispatcher().useState(initialState);
      };
      exports2.useSyncExternalStore = function(subscribe, getSnapshot, getServerSnapshot) {
        return resolveDispatcher().useSyncExternalStore(
          subscribe,
          getSnapshot,
          getServerSnapshot
        );
      };
      exports2.useTransition = function() {
        return resolveDispatcher().useTransition();
      };
      exports2.version = "19.3.1";
      "undefined" !== typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ && "function" === typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.registerInternalModuleStop && __REACT_DEVTOOLS_GLOBAL_HOOK__.registerInternalModuleStop(Error());
    })();
  }
});

// node_modules/react/index.js
var require_react = __commonJS({
  "node_modules/react/index.js"(exports2, module2) {
    "use strict";
    if (process.env.NODE_ENV === "production") {
      module2.exports = require_react_production();
    } else {
      module2.exports = require_react_development();
    }
  }
});

// node_modules/react/cjs/react-jsx-runtime.production.js
var require_react_jsx_runtime_production = __commonJS({
  "node_modules/react/cjs/react-jsx-runtime.production.js"(exports2) {
    "use strict";
    var REACT_ELEMENT_TYPE = /* @__PURE__ */ Symbol.for("react.transitional.element");
    var REACT_FRAGMENT_TYPE = /* @__PURE__ */ Symbol.for("react.fragment");
    function jsxProd(type, config, maybeKey) {
      var key = null;
      void 0 !== maybeKey && (key = "" + maybeKey);
      void 0 !== config.key && (key = "" + config.key);
      if ("key" in config) {
        maybeKey = {};
        for (var propName in config)
          "key" !== propName && (maybeKey[propName] = config[propName]);
      } else maybeKey = config;
      config = maybeKey.ref;
      return {
        $$typeof: REACT_ELEMENT_TYPE,
        type,
        key,
        ref: void 0 !== config ? config : null,
        props: maybeKey
      };
    }
    exports2.Fragment = REACT_FRAGMENT_TYPE;
    exports2.jsx = jsxProd;
    exports2.jsxs = jsxProd;
  }
});

// node_modules/react/cjs/react-jsx-runtime.development.js
var require_react_jsx_runtime_development = __commonJS({
  "node_modules/react/cjs/react-jsx-runtime.development.js"(exports2) {
    "use strict";
    "production" !== process.env.NODE_ENV && (function() {
      function getComponentNameFromType(type) {
        if (null == type) return null;
        if ("function" === typeof type)
          return type.$$typeof === REACT_CLIENT_REFERENCE ? null : type.displayName || type.name || null;
        if ("string" === typeof type) return type;
        switch (type) {
          case REACT_FRAGMENT_TYPE:
            return "Fragment";
          case REACT_PROFILER_TYPE:
            return "Profiler";
          case REACT_STRICT_MODE_TYPE:
            return "StrictMode";
          case REACT_SUSPENSE_TYPE:
            return "Suspense";
          case REACT_SUSPENSE_LIST_TYPE:
            return "SuspenseList";
          case REACT_ACTIVITY_TYPE:
            return "Activity";
          case REACT_VIEW_TRANSITION_TYPE:
            return "ViewTransition";
        }
        if ("object" === typeof type)
          switch ("number" === typeof type.tag && console.error(
            "Received an unexpected object in getComponentNameFromType(). This is likely a bug in React. Please file an issue."
          ), type.$$typeof) {
            case REACT_PORTAL_TYPE:
              return "Portal";
            case REACT_CONTEXT_TYPE:
              return type.displayName || "Context";
            case REACT_CONSUMER_TYPE:
              return (type._context.displayName || "Context") + ".Consumer";
            case REACT_FORWARD_REF_TYPE:
              var innerType = type.render;
              type = type.displayName;
              type || (type = innerType.displayName || innerType.name || "", type = "" !== type ? "ForwardRef(" + type + ")" : "ForwardRef");
              return type;
            case REACT_MEMO_TYPE:
              return innerType = type.displayName || null, null !== innerType ? innerType : getComponentNameFromType(type.type) || "Memo";
            case REACT_LAZY_TYPE:
              innerType = type._payload;
              type = type._init;
              try {
                return getComponentNameFromType(type(innerType));
              } catch (x) {
              }
          }
        return null;
      }
      function testStringCoercion(value) {
        return "" + value;
      }
      function checkKeyStringCoercion(value) {
        try {
          testStringCoercion(value);
          var JSCompiler_inline_result = false;
        } catch (e) {
          JSCompiler_inline_result = true;
        }
        if (JSCompiler_inline_result) {
          JSCompiler_inline_result = console;
          var JSCompiler_temp_const = JSCompiler_inline_result.error;
          var JSCompiler_inline_result$jscomp$0 = "function" === typeof Symbol && Symbol.toStringTag && value[Symbol.toStringTag] || value.constructor.name || "Object";
          JSCompiler_temp_const.call(
            JSCompiler_inline_result,
            "The provided key is an unsupported type %s. This value must be coerced to a string before using it here.",
            JSCompiler_inline_result$jscomp$0
          );
          return testStringCoercion(value);
        }
      }
      function getTaskName(type) {
        if (type === REACT_FRAGMENT_TYPE) return "<>";
        if ("object" === typeof type && null !== type && type.$$typeof === REACT_LAZY_TYPE)
          return "<...>";
        try {
          var name = getComponentNameFromType(type);
          return name ? "<" + name + ">" : "<...>";
        } catch (x) {
          return "<...>";
        }
      }
      function getOwner() {
        var dispatcher = ReactSharedInternals.A;
        return null === dispatcher ? null : dispatcher.getOwner();
      }
      function UnknownOwner() {
        return Error("react-stack-top-frame");
      }
      function hasValidKey(config) {
        if (hasOwnProperty.call(config, "key")) {
          var getter = Object.getOwnPropertyDescriptor(config, "key").get;
          if (getter && getter.isReactWarning) return false;
        }
        return void 0 !== config.key;
      }
      function defineKeyPropWarningGetter(props, displayName) {
        function warnAboutAccessingKey() {
          specialPropKeyWarningShown || (specialPropKeyWarningShown = true, console.error(
            "%s: `key` is not a prop. Trying to access it will result in `undefined` being returned. If you need to access the same value within the child component, you should pass it as a different prop. (https://react.dev/link/special-props)",
            displayName
          ));
        }
        warnAboutAccessingKey.isReactWarning = true;
        Object.defineProperty(props, "key", {
          get: warnAboutAccessingKey,
          configurable: true
        });
      }
      function elementRefGetterWithDeprecationWarning() {
        var componentName = getComponentNameFromType(this.type);
        didWarnAboutElementRef[componentName] || (didWarnAboutElementRef[componentName] = true, console.error(
          "Accessing element.ref was removed in React 19. ref is now a regular prop. It will be removed from the JSX Element type in a future release."
        ));
        componentName = this.props.ref;
        return void 0 !== componentName ? componentName : null;
      }
      function ReactElement(type, key, props, owner, debugStack, debugTask) {
        var refProp = props.ref;
        type = {
          $$typeof: REACT_ELEMENT_TYPE,
          type,
          key,
          props,
          _owner: owner
        };
        null !== (void 0 !== refProp ? refProp : null) ? Object.defineProperty(type, "ref", {
          enumerable: false,
          get: elementRefGetterWithDeprecationWarning
        }) : Object.defineProperty(type, "ref", { enumerable: false, value: null });
        type._store = {};
        Object.defineProperty(type._store, "validated", {
          configurable: false,
          enumerable: false,
          writable: true,
          value: 0
        });
        Object.defineProperty(type, "_debugInfo", {
          configurable: false,
          enumerable: false,
          writable: true,
          value: null
        });
        Object.defineProperty(type, "_debugStack", {
          configurable: false,
          enumerable: false,
          writable: true,
          value: debugStack
        });
        Object.defineProperty(type, "_debugTask", {
          configurable: false,
          enumerable: false,
          writable: true,
          value: debugTask
        });
        Object.freeze && (Object.freeze(type.props), Object.freeze(type));
        return type;
      }
      function jsxDEVImpl(type, config, maybeKey, isStaticChildren, debugStack, debugTask) {
        var children = config.children;
        if (void 0 !== children)
          if (isStaticChildren)
            if (isArrayImpl(children)) {
              for (isStaticChildren = 0; isStaticChildren < children.length; isStaticChildren++)
                validateChildKeys(children[isStaticChildren]);
              Object.freeze && Object.freeze(children);
            } else
              console.error(
                "React.jsx: Static children should always be an array. You are likely explicitly calling React.jsxs or React.jsxDEV. Use the Babel transform instead."
              );
          else validateChildKeys(children);
        if (hasOwnProperty.call(config, "key")) {
          children = getComponentNameFromType(type);
          var keys = Object.keys(config).filter(function(k) {
            return "key" !== k;
          });
          isStaticChildren = 0 < keys.length ? "{key: someKey, " + keys.join(": ..., ") + ": ...}" : "{key: someKey}";
          didWarnAboutKeySpread[children + isStaticChildren] || (keys = 0 < keys.length ? "{" + keys.join(": ..., ") + ": ...}" : "{}", console.error(
            'A props object containing a "key" prop is being spread into JSX:\n  let props = %s;\n  <%s {...props} />\nReact keys must be passed directly to JSX without using spread:\n  let props = %s;\n  <%s key={someKey} {...props} />',
            isStaticChildren,
            children,
            keys,
            children
          ), didWarnAboutKeySpread[children + isStaticChildren] = true);
        }
        children = null;
        void 0 !== maybeKey && (checkKeyStringCoercion(maybeKey), children = "" + maybeKey);
        hasValidKey(config) && (checkKeyStringCoercion(config.key), children = "" + config.key);
        if ("key" in config) {
          maybeKey = {};
          for (var propName in config)
            "key" !== propName && (maybeKey[propName] = config[propName]);
        } else maybeKey = config;
        children && defineKeyPropWarningGetter(
          maybeKey,
          "function" === typeof type ? type.displayName || type.name || "Unknown" : type
        );
        return ReactElement(
          type,
          children,
          maybeKey,
          getOwner(),
          debugStack,
          debugTask
        );
      }
      function validateChildKeys(node) {
        isValidElement(node) ? node._store && (node._store.validated = 1) : "object" === typeof node && null !== node && node.$$typeof === REACT_LAZY_TYPE && ("fulfilled" === node._payload.status ? isValidElement(node._payload.value) && node._payload.value._store && (node._payload.value._store.validated = 1) : node._store && (node._store.validated = 1));
      }
      function isValidElement(object) {
        return "object" === typeof object && null !== object && object.$$typeof === REACT_ELEMENT_TYPE;
      }
      var React = require_react(), REACT_ELEMENT_TYPE = /* @__PURE__ */ Symbol.for("react.transitional.element"), REACT_PORTAL_TYPE = /* @__PURE__ */ Symbol.for("react.portal"), REACT_FRAGMENT_TYPE = /* @__PURE__ */ Symbol.for("react.fragment"), REACT_STRICT_MODE_TYPE = /* @__PURE__ */ Symbol.for("react.strict_mode"), REACT_PROFILER_TYPE = /* @__PURE__ */ Symbol.for("react.profiler"), REACT_CONSUMER_TYPE = /* @__PURE__ */ Symbol.for("react.consumer"), REACT_CONTEXT_TYPE = /* @__PURE__ */ Symbol.for("react.context"), REACT_FORWARD_REF_TYPE = /* @__PURE__ */ Symbol.for("react.forward_ref"), REACT_SUSPENSE_TYPE = /* @__PURE__ */ Symbol.for("react.suspense"), REACT_SUSPENSE_LIST_TYPE = /* @__PURE__ */ Symbol.for("react.suspense_list"), REACT_MEMO_TYPE = /* @__PURE__ */ Symbol.for("react.memo"), REACT_LAZY_TYPE = /* @__PURE__ */ Symbol.for("react.lazy"), REACT_ACTIVITY_TYPE = /* @__PURE__ */ Symbol.for("react.activity"), REACT_VIEW_TRANSITION_TYPE = /* @__PURE__ */ Symbol.for("react.view_transition"), REACT_CLIENT_REFERENCE = /* @__PURE__ */ Symbol.for("react.client.reference"), ReactSharedInternals = React.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE, hasOwnProperty = Object.prototype.hasOwnProperty, isArrayImpl = Array.isArray, createTask = console.createTask ? console.createTask : function() {
        return null;
      };
      React = {
        react_stack_bottom_frame: function(callStackForError) {
          return callStackForError();
        }
      };
      var specialPropKeyWarningShown;
      var didWarnAboutElementRef = {};
      var unknownOwnerDebugStack = React.react_stack_bottom_frame.bind(
        React,
        UnknownOwner
      )();
      var unknownOwnerDebugTask = createTask(getTaskName(UnknownOwner));
      var didWarnAboutKeySpread = {};
      exports2.Fragment = REACT_FRAGMENT_TYPE;
      exports2.jsx = function(type, config, maybeKey) {
        var trackActualOwner = 1e4 > ReactSharedInternals.recentlyCreatedOwnerStacks++;
        if (trackActualOwner) {
          var previousStackTraceLimit = Error.stackTraceLimit;
          Error.stackTraceLimit = 10;
          var debugStackDEV = Error("react-stack-top-frame");
          Error.stackTraceLimit = previousStackTraceLimit;
        } else debugStackDEV = unknownOwnerDebugStack;
        return jsxDEVImpl(
          type,
          config,
          maybeKey,
          false,
          debugStackDEV,
          trackActualOwner ? createTask(getTaskName(type)) : unknownOwnerDebugTask
        );
      };
      exports2.jsxs = function(type, config, maybeKey) {
        var trackActualOwner = 1e4 > ReactSharedInternals.recentlyCreatedOwnerStacks++;
        if (trackActualOwner) {
          var previousStackTraceLimit = Error.stackTraceLimit;
          Error.stackTraceLimit = 10;
          var debugStackDEV = Error("react-stack-top-frame");
          Error.stackTraceLimit = previousStackTraceLimit;
        } else debugStackDEV = unknownOwnerDebugStack;
        return jsxDEVImpl(
          type,
          config,
          maybeKey,
          true,
          debugStackDEV,
          trackActualOwner ? createTask(getTaskName(type)) : unknownOwnerDebugTask
        );
      };
    })();
  }
});

// node_modules/react/jsx-runtime.js
var require_jsx_runtime = __commonJS({
  "node_modules/react/jsx-runtime.js"(exports2, module2) {
    "use strict";
    if (process.env.NODE_ENV === "production") {
      module2.exports = require_react_jsx_runtime_production();
    } else {
      module2.exports = require_react_jsx_runtime_development();
    }
  }
});

// src/lib/routes.ts
var route = (id, depth, name) => ({
  id,
  path: `/${id}`,
  depth,
  name
});
var ROUTES = [
  { id: "title", path: "/", depth: 0, name: "\u6E29\u6696\u5982\u521D" },
  // 从标题页直达
  route("main", 1, "\u6E38\u620F"),
  route("setup", 1, "\u5F00\u5C40\u8BBE\u7F6E"),
  route("select", 1, "\u9009\u62E9\u89D2\u8272"),
  route("save", 1, "\u5B58\u6863\u7BA1\u7406"),
  route("settings", 1, "\u8BBE\u7F6E"),
  route("phone", 1, "\u624B\u673A"),
  route("worldbook", 1, "\u4E16\u754C\u4E66"),
  route("pet", 1, "\u684C\u5BA0"),
  route("gallery", 1, "\u56DE\u5FC6\u5F55"),
  route("roster", 1, "\u89D2\u8272\u5E93"),
  route("novelselect", 1, "\u5E73\u884C\u5B87\u5B99"),
  route("showcase-list", 1, "\u5F00\u53D1\u8005\u5C55\u793A\u6A21\u5F0F"),
  // 设置分类子页与它的详情
  route("settingssection", 2, "\u8BBE\u7F6E"),
  route("usagestats", 3, "\u7528\u91CF\u7EDF\u8BA1"),
  route("petsettings", 3, "\u684C\u5BA0\u8BBE\u7F6E"),
  route("petstudio", 4, "AI \u5236\u4F5C\u684C\u5BA0"),
  route("colorpicker", 4, "\u9009\u4E2A\u989C\u8272"),
  route("mentalHealth", 4, "\u5FC3\u7406\u72B6\u51B5"),
  route("aiProviders", 4, "AI \u5382\u5546"),
  route("aiModels", 4, "\u6A21\u578B\u7BA1\u7406"),
  route("modelDeployment", 5, "\u6A21\u578B\u90E8\u7F72"),
  route("pluginCenter", 4, "\u63D2\u4EF6\u4E2D\u5FC3"),
  route("pluginSettings", 5, "\u63D2\u4EF6\u8BBE\u7F6E"),
  route("pluginWeb", 6, "\u63D2\u4EF6\u9875\u9762"),
  route("pluginPerms", 5, "\u63D2\u4EF6\u6743\u9650"),
  route("saveRules", 3, "\u5B58\u6863\u9AD8\u7EA7\u8BBE\u7F6E"),
  route("cloudsaves", 3, "\u4E91\u5B58\u6863"),
  // 存档
  route("saveTree", 2, "\u5206\u652F\u6811"),
  // 角色
  route("newcharacter", 2, "\u65B0\u5EFA\u89D2\u8272"),
  route("studio", 2, "\u89D2\u8272\u5DE5\u4F5C\u5BA4"),
  route("figurePack", 3, "\u5F62\u8C61\u5305"),
  route("outfit", 3, "\u6362\u88C5"),
  route("materials", 2, "\u7D20\u6750\u5E93"),
  route("portraitCalibration", 3, "\u7ACB\u7ED8\u6807\u5B9A"),
  route("avatarcrop", 3, "\u5934\u50CF\u88C1\u526A"),
  route("subProfile", 2, "\u4E3B\u8BBE\u5B9A \xD7 \u526F\u8BBE\u5B9A"),
  route("memoryManage", 2, "\u8BB0\u5FC6\u7BA1\u7406"),
  route("memoryGraph", 3, "\u5173\u7CFB\u7F51"),
  route("memorySearch", 3, "\u641C\u4E8B\u4EF6"),
  // 剧情
  route("newrun", 2, "\u65B0\u5F00\u4E00\u5C40"),
  route("chatimport", 2, "\u5BFC\u5165\u5BF9\u8BDD"),
  route("scenelibrary", 2, "\u573A\u666F\u5DE5\u574A"),
  route("SceneCalibrationPage", 3, "\u80CC\u666F\u6807\u5B9A"),
  route("forcecast", 3, "\u70B9\u540D \xB7 \u8FDB\u5165\u5267\u60C5"),
  route("showcase-edit", 2, "\u7F16\u8F91\u5C55\u793A\u6848"),
  route("showcase-play", 3, "\u64AD\u653E\u5C55\u793A\u6848"),
  route("novelworld", 2, "\u4E16\u754C"),
  // 游戏内
  route("game", 2, "\u5C0F\u6E38\u620F"),
  route("map", 2, "\u5730\u56FE"),
  route("mapcreate", 3, "\u521B\u5EFA\u5730\u56FE"),
  route("mapcapture", 3, "\u53D6\u666F\u4E0E\u5BFC\u5165"),
  route("minimal3d", 3, "3D \u7070\u5EA6\u89C6\u56FE"),
  route("flows", 3, "\u6D41\u7A0B\u56FE"),
  route("shop", 2, "\u5546\u5E97"),
  route("affection-level-up", 3, "\u597D\u611F\u63D0\u5347"),
  route("cg-view", 3, "CG"),
  // 聊天
  route("chat", 2, "\u5FAE\u4FE1"),
  route("qqchat", 2, "QQ"),
  route("qqforum", 3, "\u8BBA\u575B"),
  route("bindwizard", 2, "\u7ED1\u5B9A\u5411\u5BFC"),
  // 监测
  route("superMonitor", 2, "\u8D85\u7EA7\u76D1\u6D4B"),
  route("moduleInspector", 3, "\u6A21\u5757\u8BE6\u60C5")
];
var BY_PATH = new Map(ROUTES.map((r) => [r.path, r]));
var FALLBACK = ROUTES[0];

// src/screens/character-media/gallery.tsx
var import_react2 = __toESM(require_react(), 1);

// src/components/PageShell.tsx
var import_jsx_runtime = __toESM(require_jsx_runtime(), 1);
function PageShell({ children }) {
  return /* @__PURE__ */ (0, import_jsx_runtime.jsx)("div", { className: "page", children });
}

// src/components/Glass.tsx
var import_react = __toESM(require_react(), 1);

// src/lib/motion.ts
var PRESS_MS = 120;
var prefersReducedMotion = () => typeof window !== "undefined" && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
var canonical = (ms) => prefersReducedMotion() ? 0 : ms;
var frames = /* @__PURE__ */ new Set();
var raf = 0;
function tick(now) {
  raf = 0;
  let busy = false;
  for (const frame of frames) {
    if (frame(now)) busy = true;
  }
  if (busy) raf = requestAnimationFrame(tick);
}
function kick() {
  if (!raf) raf = requestAnimationFrame(tick);
}
function onFrame(frame) {
  frames.add(frame);
  kick();
  return () => {
    frames.delete(frame);
  };
}
var easeOutCubic = (k) => 1 - Math.pow(1 - k, 3);
function tween(from, to, ms, apply, done) {
  const keys = Object.keys(to);
  const values = { ...from };
  if (ms <= 0) {
    apply(to);
    done?.();
    return { cancel: () => void 0 };
  }
  const started = performance.now();
  let alive = true;
  let stop = null;
  stop = onFrame((now) => {
    if (!alive) return false;
    const k = Math.min(1, Math.max(0, (now - started) / ms));
    const eased = easeOutCubic(k);
    for (const key of keys) {
      const a = from[key] ?? 0;
      const b = to[key] ?? 0;
      values[key] = a + (b - a) * eased;
    }
    apply(values);
    if (k >= 1) {
      alive = false;
      stop?.();
      done?.();
      return false;
    }
    return true;
  });
  return {
    cancel() {
      alive = false;
      stop?.();
    }
  };
}

// src/lib/light.ts
var LIGHT_R = 520;
var LIGHT_EXP = 1.35;
var samples = /* @__PURE__ */ new Set();
var pointer = { x: 0, y: 0, tx: 0, ty: 0, known: false };
function measure(sample) {
  const r = sample.el.getBoundingClientRect();
  sample.rect = { left: r.left, top: r.top, width: r.width, height: r.height };
}
function registerGlass(el) {
  const sample = {
    el,
    rect: { left: 0, top: 0, width: 0, height: 0 },
    observer: new ResizeObserver(() => {
      measure(sample);
      paint(pointer.x, pointer.y);
    })
  };
  measure(sample);
  sample.observer.observe(el);
  samples.add(sample);
  if (pointer.known) paint(pointer.x, pointer.y);
  return () => {
    sample.observer.disconnect();
    samples.delete(sample);
  };
}
function lightAt(px, py, rect) {
  const cx = Math.min(Math.max(px, rect.left), rect.left + rect.width);
  const cy = Math.min(Math.max(py, rect.top), rect.top + rect.height);
  const d = Math.hypot(px - cx, py - cy);
  const intensity = Math.pow(Math.max(0, 1 - d / LIGHT_R), LIGHT_EXP);
  return { x: cx - rect.left, y: cy - rect.top, d, intensity };
}
function paint(px, py) {
  for (const sample of samples) {
    const { x, y, intensity } = lightAt(px, py, sample.rect);
    const style = sample.el.style;
    style.setProperty("--lx", `${x.toFixed(1)}px`);
    style.setProperty("--ly", `${y.toFixed(1)}px`);
    style.setProperty("--li", intensity.toFixed(3));
  }
}

// src/components/Glass.tsx
var import_jsx_runtime2 = __toESM(require_jsx_runtime(), 1);
function Glass({
  as = "div",
  tone = "card",
  radius = 8,
  className = "",
  children,
  style,
  elementRef,
  ...rest
}) {
  const inner = (0, import_react.useRef)(null);
  const ref = elementRef ?? inner;
  (0, import_react.useEffect)(() => {
    const el = ref.current;
    if (!el) return;
    return registerGlass(el);
  }, [ref]);
  return (0, import_react.createElement)(
    as,
    {
      ref,
      className: `glass glass--${tone} ${className}`.trim(),
      style: { ...style, "--r": `${radius}px` },
      ...rest
    },
    /* @__PURE__ */ (0, import_jsx_runtime2.jsxs)("div", { className: "glass__layers", "aria-hidden": "true", children: [
      /* @__PURE__ */ (0, import_jsx_runtime2.jsx)("div", { className: "glass__refract", children: /* @__PURE__ */ (0, import_jsx_runtime2.jsx)("i", {}) }),
      /* @__PURE__ */ (0, import_jsx_runtime2.jsx)("div", { className: "glass__wall" }),
      /* @__PURE__ */ (0, import_jsx_runtime2.jsx)("div", { className: "glass__cyl glass__cyl--top", children: /* @__PURE__ */ (0, import_jsx_runtime2.jsx)("i", {}) }),
      /* @__PURE__ */ (0, import_jsx_runtime2.jsx)("div", { className: "glass__cyl glass__cyl--bottom", children: /* @__PURE__ */ (0, import_jsx_runtime2.jsx)("i", {}) }),
      /* @__PURE__ */ (0, import_jsx_runtime2.jsx)("div", { className: "glass__face" }),
      /* @__PURE__ */ (0, import_jsx_runtime2.jsx)("div", { className: "glass__soft" }),
      /* @__PURE__ */ (0, import_jsx_runtime2.jsx)("div", { className: "glass__sheen" })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime2.jsx)("div", { className: "glass__content", children })
  );
}

// src/components/kit.tsx
var import_jsx_runtime3 = __toESM(require_jsx_runtime(), 1);
function PageHeader({
  title: title2,
  subtitle,
  eyebrow: eyebrow2,
  onBack,
  backText = "< \u8FD4\u56DE",
  right,
  variant = "bar"
}) {
  const inner = /* @__PURE__ */ (0, import_jsx_runtime3.jsxs)("div", { className: "ph__inner", children: [
    onBack ? /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("button", { type: "button", className: "ph__back", onClick: onBack, children: backText }) : /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("span", {}),
    /* @__PURE__ */ (0, import_jsx_runtime3.jsxs)("div", { className: "ph__mid", children: [
      eyebrow2 ? /* @__PURE__ */ (0, import_jsx_runtime3.jsxs)("span", { className: "ph__eyebrow", children: [
        /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("i", { className: "ph__mark", "aria-hidden": "true" }),
        eyebrow2
      ] }) : null,
      /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("h1", { className: "ph__title", "data-page-title": true, tabIndex: -1, children: title2 }),
      subtitle ? /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("span", { className: "ph__sub", title: subtitle, children: subtitle }) : null
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("div", { className: "ph__right", children: right })
  ] });
  if (variant === "ground") {
    return /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("header", { className: "ph ph--ground", children: inner });
  }
  return /* @__PURE__ */ (0, import_jsx_runtime3.jsx)(Glass, { as: "header", tone: "bar", radius: 0, className: "ph ph--bar", children: inner });
}
function Card({
  title: title2,
  note,
  right,
  children,
  tight,
  className = ""
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime3.jsxs)(Glass, { as: "section", tone: "card", radius: 12, className: `k-card${tight ? " k-card--tight" : ""} ${className}`.trim(), children: [
    title2 || right ? /* @__PURE__ */ (0, import_jsx_runtime3.jsxs)("div", { className: "k-card__head", children: [
      /* @__PURE__ */ (0, import_jsx_runtime3.jsxs)("div", { children: [
        title2 ? /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("h2", { className: "k-card__title", children: title2 }) : null,
        note ? /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("p", { className: "k-card__note", children: note }) : null
      ] }),
      right ? /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("div", { className: "k-card__right", children: right }) : null
    ] }) : null,
    children
  ] });
}
function Row({
  title: title2,
  desc: desc2,
  right,
  onClick,
  chevron,
  disabled,
  className = ""
}) {
  const body = /* @__PURE__ */ (0, import_jsx_runtime3.jsxs)(import_jsx_runtime3.Fragment, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime3.jsxs)("div", { className: "k-row__text", children: [
      /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("span", { className: "k-row__title", children: title2 }),
      desc2 ? /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("span", { className: "k-row__desc", children: desc2 }) : null
    ] }),
    right ? /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("div", { className: "k-row__right", children: right }) : null,
    chevron ? /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("span", { className: "k-row__chev", "aria-hidden": "true", children: "\u203A" }) : null
  ] });
  if (!onClick) {
    return /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("div", { className: `k-row ${className}`.trim(), children: body });
  }
  return /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("button", { type: "button", className: `k-row k-row--tap ${className}`.trim(), onClick, disabled, children: body });
}
function Sep({ bleed }) {
  return /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("div", { className: bleed ? "k-sep k-sep--bleed" : "k-sep", role: "presentation" });
}
function GroupLabel({ children }) {
  return /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("h3", { className: "k-group", children });
}
function TextBtn({
  children,
  onClick,
  disabled,
  tone = "plain"
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime3.jsx)(
    "button",
    {
      type: "button",
      className: tone === "signal" ? "k-textbtn k-textbtn--signal" : "k-textbtn",
      onClick,
      disabled,
      children
    }
  );
}
function Chip({ children, tone = "quiet" }) {
  return /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("span", { className: `k-chip k-chip--${tone}`, children });
}
function Empty({ title: title2, reason, hint }) {
  return /* @__PURE__ */ (0, import_jsx_runtime3.jsxs)("div", { className: "k-empty", children: [
    /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("span", { className: "k-empty__title", children: title2 }),
    reason ? /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("span", { className: "k-empty__reason", children: reason }) : null,
    hint ? /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("span", { className: "k-empty__hint", children: hint }) : null
  ] });
}
function Grid2({ children, className = "" }) {
  return /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("div", { className: `k-grid2 ${className}`.trim(), children });
}
function Field({
  value,
  onChange,
  placeholder,
  label,
  type = "text",
  multiline
}) {
  if (multiline) {
    return /* @__PURE__ */ (0, import_jsx_runtime3.jsx)(
      "textarea",
      {
        className: "k-field k-field--area",
        value,
        placeholder,
        "aria-label": label ?? placeholder,
        onChange: (event) => onChange(event.target.value)
      }
    );
  }
  return /* @__PURE__ */ (0, import_jsx_runtime3.jsx)(
    "input",
    {
      className: "k-field",
      type,
      value,
      placeholder,
      "aria-label": label ?? placeholder,
      onChange: (event) => onChange(event.target.value)
    }
  );
}
function Select({
  value,
  options: options2,
  onChange,
  label
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime3.jsxs)("span", { className: "k-select", children: [
    /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("select", { value, "aria-label": label, onChange: (event) => onChange(event.target.value), children: options2.map((option) => /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("option", { value: option.value, children: option.label }, option.value)) }),
    /* @__PURE__ */ (0, import_jsx_runtime3.jsx)("i", { "aria-hidden": "true" })
  ] });
}

// src/screens/character-media/parts.tsx
var import_jsx_runtime4 = __toESM(require_jsx_runtime(), 1);
function Sub({ children }) {
  return /* @__PURE__ */ (0, import_jsx_runtime4.jsx)(
    "h2",
    {
      style: {
        margin: 0,
        fontSize: "var(--w-size-subhead)",
        fontWeight: 600,
        color: "var(--w-ink-display)",
        lineHeight: 1.4
      },
      children
    }
  );
}
function SectionTitle({ children }) {
  return /* @__PURE__ */ (0, import_jsx_runtime4.jsx)(
    "h3",
    {
      style: {
        margin: "26px 0 12px 4px",
        fontSize: "var(--w-size-small)",
        fontWeight: 700,
        color: "var(--w-ink-body)"
      },
      children
    }
  );
}
function Cap({ children, style }) {
  return /* @__PURE__ */ (0, import_jsx_runtime4.jsx)(
    "p",
    {
      style: {
        margin: 0,
        fontSize: "var(--w-size-caption)",
        color: "var(--w-ink-muted)",
        lineHeight: 1.6,
        ...style
      },
      children
    }
  );
}
function Muted({ children }) {
  return /* @__PURE__ */ (0, import_jsx_runtime4.jsx)("span", { style: { fontSize: "var(--w-size-caption)", color: "var(--w-ink-faint)", lineHeight: 1.6 }, children });
}
function PickerPlaceholder({ title: title2 }) {
  return /* @__PURE__ */ (0, import_jsx_runtime4.jsxs)(
    "div",
    {
      style: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        gap: "var(--w-space-2)",
        boxSizing: "border-box",
        width: "100%",
        padding: "7px 12px",
        border: "1px solid var(--w-hairline)",
        borderRadius: "var(--w-radius-button)",
        background: "var(--w-surface-tint)",
        fontSize: "var(--w-size-small)",
        color: "var(--w-ink-faint)"
      },
      children: [
        /* @__PURE__ */ (0, import_jsx_runtime4.jsx)("span", { children: title2 }),
        /* @__PURE__ */ (0, import_jsx_runtime4.jsx)(
          "span",
          {
            "aria-hidden": "true",
            style: {
              width: 0,
              height: 0,
              borderLeft: "4px solid transparent",
              borderRight: "4px solid transparent",
              borderTop: "5px solid var(--w-ink-muted)"
            }
          }
        )
      ]
    }
  );
}
function Well({
  width,
  height,
  ratio,
  caption: caption7,
  radius = "var(--w-radius-well)",
  children,
  style
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime4.jsx)(
    "div",
    {
      style: {
        width,
        height,
        aspectRatio: ratio,
        boxSizing: "border-box",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        borderRadius: radius,
        border: "1px solid var(--w-hairline)",
        background: "var(--w-surface-tint)",
        overflow: "hidden",
        ...style
      },
      children: children ?? (caption7 ? /* @__PURE__ */ (0, import_jsx_runtime4.jsx)("span", { style: { fontSize: "var(--w-size-caption)", color: "var(--w-ink-faint)" }, children: caption7 }) : null)
    }
  );
}
function GhostSm({
  children,
  onClick,
  disabled,
  width,
  style
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime4.jsx)(
    "button",
    {
      type: "button",
      className: "ghost",
      onClick,
      disabled,
      style: {
        height: 34,
        padding: "0 12px",
        fontSize: "var(--w-size-caption)",
        width,
        ...style
      },
      children
    }
  );
}
function PrimarySm({
  children,
  onClick,
  disabled,
  style
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime4.jsx)(
    "button",
    {
      type: "button",
      onClick,
      disabled,
      style: {
        height: 34,
        padding: "0 16px",
        border: 0,
        borderRadius: "var(--w-radius-button)",
        background: "var(--w-signal)",
        color: "var(--w-ink-on-signal)",
        font: "inherit",
        fontSize: "var(--w-size-small)",
        fontWeight: 700,
        cursor: disabled ? "default" : "pointer",
        opacity: disabled ? 0.5 : 1,
        whiteSpace: "nowrap",
        ...style
      },
      children
    }
  );
}
function TextSm({
  children,
  onClick,
  danger,
  bold,
  style
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime4.jsx)(
    "button",
    {
      type: "button",
      className: "k-textbtn",
      onClick,
      style: {
        fontSize: "var(--w-size-caption)",
        color: danger ? "var(--w-signal)" : void 0,
        fontWeight: bold ? 700 : void 0,
        whiteSpace: "nowrap",
        ...style
      },
      children
    }
  );
}
function NoteRow({ children }) {
  return /* @__PURE__ */ (0, import_jsx_runtime4.jsx)("div", { className: "k-row", children: /* @__PURE__ */ (0, import_jsx_runtime4.jsx)("div", { className: "k-row__text", children: /* @__PURE__ */ (0, import_jsx_runtime4.jsx)("span", { className: "k-row__desc", children }) }) });
}
function RowLine({
  title: title2,
  desc: desc2,
  right,
  onClick,
  chevron
}) {
  const body = /* @__PURE__ */ (0, import_jsx_runtime4.jsxs)(import_jsx_runtime4.Fragment, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime4.jsxs)("div", { className: "k-row__text", children: [
      /* @__PURE__ */ (0, import_jsx_runtime4.jsx)("span", { className: "k-row__title", children: title2 }),
      desc2 ? /* @__PURE__ */ (0, import_jsx_runtime4.jsx)("span", { className: "k-row__desc", children: desc2 }) : null
    ] }),
    right ? /* @__PURE__ */ (0, import_jsx_runtime4.jsx)("div", { className: "k-row__right", children: right }) : null,
    chevron ? /* @__PURE__ */ (0, import_jsx_runtime4.jsx)("span", { className: "k-row__chev", "aria-hidden": "true", children: "\u203A" }) : null
  ] });
  if (!onClick) return /* @__PURE__ */ (0, import_jsx_runtime4.jsx)("div", { className: "k-row", children: body });
  return /* @__PURE__ */ (0, import_jsx_runtime4.jsx)("button", { type: "button", className: "k-row k-row--tap", onClick, children: body });
}

// src/screens/character-media/gallery.tsx
var import_jsx_runtime5 = __toESM(require_jsx_runtime(), 1);
var TABS = [
  { id: "memoir", label: "\u56DE\u5FC6\u5F55" },
  { id: "remembered", label: "\u8BB0\u5F97\u7684\u4E8B" },
  { id: "diary", label: "\u65E5\u8BB0" },
  { id: "cg", label: "CG\u6536\u85CF\u518C" }
];
function Gallery({ navigate }) {
  const [tab, setTab] = (0, import_react2.useState)("memoir");
  const [search, setSearch] = (0, import_react2.useState)("");
  return /* @__PURE__ */ (0, import_jsx_runtime5.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime5.jsx)(
      PageHeader,
      {
        variant: "ground",
        title: "\u56DE\u5FC6\u5F55",
        subtitle: "\u5168\u90E8\u597D\u611F\u65F6\u523B\u4E0E\u5168\u90E8\u5BF9\u8BDD\u90FD\u5728\u8FD9\u91CC\uFF0C\u53EF\u641C\u7D22\uFF1B\u65E5\u8BB0\u662F\u6BCF\u5929\u81EA\u52A8\u751F\u6210\u7684\u966A\u4F34\u603B\u7ED3\uFF08\u6838\u5FC3\u673A\u5236\uFF0C\u65E0\u9700\u5F00\u542F\uFF09"
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime5.jsxs)("div", { className: "screen", children: [
      /* @__PURE__ */ (0, import_jsx_runtime5.jsx)("div", { style: { display: "grid", gridTemplateColumns: "repeat(4, minmax(0, 1fr))", gap: "var(--w-space-2)" }, children: TABS.map((item) => /* @__PURE__ */ (0, import_jsx_runtime5.jsx)(
        "button",
        {
          type: "button",
          "aria-pressed": tab === item.id,
          onClick: () => setTab(item.id),
          style: {
            minHeight: 40,
            padding: "10px 0",
            border: 0,
            borderRadius: "var(--w-radius-well)",
            background: tab === item.id ? "var(--w-surface-tint)" : "transparent",
            color: "var(--w-ink-display)",
            font: "inherit",
            fontSize: "var(--w-size-small)",
            fontWeight: tab === item.id ? 700 : 400,
            cursor: "pointer"
          },
          children: item.label
        },
        item.id
      )) }),
      tab === "remembered" ? /* @__PURE__ */ (0, import_jsx_runtime5.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-3)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime5.jsx)(Cap, { children: "\u8FD9\u662F TA \u8111\u5B50\u91CC\u771F\u6B63\u8BB0\u5F97\u7684\u4E8B\u2014\u2014\u91CD\u8981\u7684\u5FD8\u5F97\u6162\uFF0C\u7410\u788E\u7684\u81EA\u7136\u6DE1\u5FD8\uFF1B\u7167\u7247\u5F0F\u8BB0\u5FC6\u53EA\u6709\u5355\u5F20\u3002" }),
        /* @__PURE__ */ (0, import_jsx_runtime5.jsxs)(Card, { tight: true, children: [
          /* @__PURE__ */ (0, import_jsx_runtime5.jsx)(
            Row,
            {
              title: "\u8BB0\u5FC6\u7BA1\u7406\uFF08\u6807\u7B7E / \u8BB0\u5F97\u7684\u4E8B / \u526F\u8BBE\u5B9A\uFF09",
              chevron: true,
              onClick: () => navigate("/memoryManage")
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime5.jsx)(Sep, {}),
          /* @__PURE__ */ (0, import_jsx_runtime5.jsx)(Row, { title: "\u6253\u5F00\u5173\u7CFB\u7F51", chevron: true, onClick: () => navigate("/memoryGraph") }),
          /* @__PURE__ */ (0, import_jsx_runtime5.jsx)(Sep, {}),
          /* @__PURE__ */ (0, import_jsx_runtime5.jsx)(Row, { title: "\u641C\u4E8B\u4EF6", chevron: true, onClick: () => navigate("/memorySearch") })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime5.jsx)(Cap, { children: "\u5173\u7CFB\u7F51\u548C\u641C\u4E8B\u4EF6\u5404\u5F00\u4E00\u6247\u7A97\uFF0C\u4E3B\u7A97\u53E3\u7EE7\u7EED\u804A\u3002" }),
        /* @__PURE__ */ (0, import_jsx_runtime5.jsx)(Card, { tight: true, children: /* @__PURE__ */ (0, import_jsx_runtime5.jsx)(NoteRow, { children: "\u8FD8\u6CA1\u6709\u8BB0\u5F97\u7684\u4E8B\u2014\u2014\u591A\u804A\u51E0\u53E5\uFF0C\u91CD\u8981\u7684\u4F1A\u88AB\u8BB0\u4E0B\u6765\u3002" }) })
      ] }) : null,
      tab === "memoir" ? /* @__PURE__ */ (0, import_jsx_runtime5.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-3)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime5.jsx)(Card, { children: /* @__PURE__ */ (0, import_jsx_runtime5.jsxs)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime5.jsx)("div", { style: { flex: 1, minWidth: 0 }, children: /* @__PURE__ */ (0, import_jsx_runtime5.jsx)(
            "input",
            {
              className: "k-field",
              value: search,
              placeholder: "\u641C\u7D22\u5BF9\u8BDD\u56DE\u5FC6\u2026",
              "aria-label": "\u641C\u7D22\u5BF9\u8BDD\u56DE\u5FC6\u2026",
              onChange: (event) => setSearch(event.target.value),
              style: { fontSize: "var(--w-size-small)" }
            }
          ) }),
          /* @__PURE__ */ (0, import_jsx_runtime5.jsx)("button", { type: "button", className: "k-textbtn", onClick: () => setSearch(""), children: "\u6E05\u7A7A" })
        ] }) }),
        /* @__PURE__ */ (0, import_jsx_runtime5.jsx)(SectionTitle, { children: "\u597D\u611F\u65F6\u523B\uFF08\u5168\u90E8\uFF09" }),
        /* @__PURE__ */ (0, import_jsx_runtime5.jsx)(Card, { tight: true, children: /* @__PURE__ */ (0, import_jsx_runtime5.jsx)(NoteRow, { children: "\u8FD8\u6CA1\u6709\u597D\u611F\u65F6\u523B\uFF0C\u53BB\u548C TA \u4E92\u52A8\u5427~" }) }),
        /* @__PURE__ */ (0, import_jsx_runtime5.jsx)(SectionTitle, { children: "\u5BF9\u8BDD\u56DE\u5FC6\uFF08\u53EF\u641C\u7D22\uFF09" }),
        /* @__PURE__ */ (0, import_jsx_runtime5.jsx)(Card, { tight: true, children: /* @__PURE__ */ (0, import_jsx_runtime5.jsx)(NoteRow, { children: "\u8FD8\u6CA1\u6709\u5BF9\u8BDD\u56DE\u5FC6" }) })
      ] }) : null,
      tab === "diary" ? /* @__PURE__ */ (0, import_jsx_runtime5.jsx)(Card, { tight: true, children: /* @__PURE__ */ (0, import_jsx_runtime5.jsx)(NoteRow, { children: "\u8FD8\u6CA1\u6709\u65E5\u8BB0\uFF0C\u4ECA\u5929\u591A\u966A\u966A TA\uFF0C\u660E\u5929\u65E9\u4E0A\u5C31\u4F1A\u6709\u4E00\u7BC7\u603B\u7ED3\u5566~" }) }) : null,
      tab === "cg" ? /* @__PURE__ */ (0, import_jsx_runtime5.jsx)(Cap, { children: "\u8FD8\u6CA1\u6709\u6536\u85CF\u7684 CG\u3002\u5F53 TA \u5728\u5BF9\u8BDD\u91CC\u5F15\u7528\u3010CG:\u6587\u4EF6\u540D\u3011\u65F6\u5C31\u4F1A\u89E3\u9501\u6536\u85CF~" }) : null
    ] })
  ] });
}

// src/screens/character-media/outfit.tsx
var import_react3 = __toESM(require_react(), 1);
var import_jsx_runtime6 = __toESM(require_jsx_runtime(), 1);
function Outfit({ back }) {
  const [prompt, setPrompt] = (0, import_react3.useState)("");
  return /* @__PURE__ */ (0, import_jsx_runtime6.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime6.jsx)(PageHeader, { title: "\u6362\u88C5", onBack: back }),
    /* @__PURE__ */ (0, import_jsx_runtime6.jsxs)("div", { className: "screen", style: { maxWidth: 900, gap: "var(--w-space-3)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime6.jsxs)(Card, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime6.jsx)(Sub, { children: "\u8FD8\u6CA1\u6709\u8FDB\u5165\u89D2\u8272" }),
        /* @__PURE__ */ (0, import_jsx_runtime6.jsx)(Well, { height: 360, caption: "\u7ACB\u7ED8", radius: "var(--w-radius-card)" }),
        /* @__PURE__ */ (0, import_jsx_runtime6.jsx)(Cap, { children: "\u5F53\u524D\uFF1A" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime6.jsx)(Cap, { style: { color: "var(--w-signal)" }, children: "\u5148\u5728\u89D2\u8272\u9009\u62E9\u9875\u5F00\u59CB\u4E00\u5C40\uFF0C\u6216\u628A\u67D0\u4E2A\u89D2\u8272\u8BBE\u4E3A\u4E3B\u89D2\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime6.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-3)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime6.jsx)(Sub, { children: "\u5DF2\u6709\u7684\u88C5\u626E" }),
        /* @__PURE__ */ (0, import_jsx_runtime6.jsx)(Card, { tight: true, children: /* @__PURE__ */ (0, import_jsx_runtime6.jsx)(NoteRow, { children: "\u8FD9\u4E2A\u89D2\u8272\u8FD8\u6CA1\u6709\u7ACB\u7ED8\u3002\u53EF\u4EE5\u5728\u4E0B\u9762\u7528\u4E00\u53E5\u63CF\u8FF0\u751F\u6210\u7B2C\u4E00\u5957\u88C5\u626E\u3002" }) })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime6.jsxs)(Card, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime6.jsx)(Sub, { children: "\u7528\u4E00\u53E5\u8BDD\u751F\u6210\u65B0\u88C5\u626E" }),
        /* @__PURE__ */ (0, import_jsx_runtime6.jsx)(Cap, { children: "\u63CF\u8FF0\u8863\u670D\u4E0E\u573A\u666F\u5373\u53EF\uFF08\u4F8B\u5982\uFF1A\u767D\u8272\u9488\u7EC7\u886B\u914D\u6D45\u8272\u957F\u88D9\uFF0C\u7AD9\u5728\u5348\u540E\u7A97\u8FB9\uFF09\u3002\u751F\u6210\u7684\u662F\u5168\u8EAB\u7ACB\u7ED8\uFF0C\u4F1A\u76F4\u63A5\u7A7F\u4E0A\u5E76\u767B\u8BB0\u8FDB\u8FD9\u4E2A\u89D2\u8272\u3002" }),
        /* @__PURE__ */ (0, import_jsx_runtime6.jsx)(
          "input",
          {
            className: "k-field",
            value: prompt,
            placeholder: "\u60F3\u7ED9 TA \u7A7F\u4EC0\u4E48\uFF1F",
            "aria-label": "\u60F3\u7ED9 TA \u7A7F\u4EC0\u4E48\uFF1F",
            onChange: (event) => setPrompt(event.target.value),
            style: { fontSize: 13 }
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime6.jsx)("div", { children: /* @__PURE__ */ (0, import_jsx_runtime6.jsx)(PrimarySm, { children: "\u751F\u6210\u5E76\u7A7F\u4E0A" }) }),
        /* @__PURE__ */ (0, import_jsx_runtime6.jsx)(Muted, { children: "\u6CA1\u6709\u5F00\u542F\u753B\u9762\u751F\u6210\uFF1F\u53BB\u8BBE\u7F6E \u2192 AI \u2192 \u753B\u9762\u751F\u6210\u586B\u63A5\u53E3\u4E0E\u5BC6\u94A5\u3002" })
      ] })
    ] })
  ] });
}

// src/screens/character-media/figurePack.tsx
var import_react4 = __toESM(require_react(), 1);
var import_jsx_runtime7 = __toESM(require_jsx_runtime(), 1);
var FIELD_LABEL = {
  margin: "0 0 4px 2px",
  fontSize: "var(--w-size-caption)",
  color: "var(--w-ink-muted)"
};
var FIELD = { fontSize: "var(--w-size-small)" };
var SLOTS = ["base", "hair", "top", "bottom", "shoes", "accessory"];
function FigurePack({ back }) {
  const [packId, setPackId] = (0, import_react4.useState)("");
  const [name, setName] = (0, import_react4.useState)("");
  const [prompt, setPrompt] = (0, import_react4.useState)("");
  const [rows2, setRows] = (0, import_react4.useState)("1");
  const [columns, setColumns] = (0, import_react4.useState)("4");
  const [slot, setSlot] = (0, import_react4.useState)("top");
  const [motionId, setMotionId] = (0, import_react4.useState)("walk");
  const [motionFps, setMotionFps] = (0, import_react4.useState)("8");
  return /* @__PURE__ */ (0, import_jsx_runtime7.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(PageHeader, { title: "\u5F62\u8C61\u5305", subtitle: "\u65B0\u7248\u7740\u88C5 \xB7 \u7ED1\u5B9A\u540E\u4E3B\u754C\u9762\u4E0E\u5DE5\u4F5C\u5BA4\u4E00\u8D77\u6362", onBack: back }),
    /* @__PURE__ */ (0, import_jsx_runtime7.jsxs)("div", { className: "screen", style: { maxWidth: 880, gap: "var(--w-space-3)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime7.jsxs)(Card, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(Sub, { children: "\u7ED1\u7ED9\u8C01" }),
        /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(PickerPlaceholder, { title: "\u9009\u62E9\u89D2\u8272" }),
        /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(Cap, { children: "\u7ED1\u5B9A\u53EA\u6539\u8FD9\u4E2A\u89D2\u8272\u7684\u5F62\u8C61\u6765\u6E90\uFF1B\u65E7\u7248 SpriteMap \u7ACB\u7ED8\u4E0D\u4F1A\u88AB\u5220\uFF0C\u968F\u65F6\u80FD\u89E3\u7ED1\u56DE\u53BB\u3002" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime7.jsxs)(Card, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime7.jsx)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-2)" }, children: /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(Sub, { children: "\u9884\u89C8\u4E0E\u52A8\u4F5C" }) }),
        /* @__PURE__ */ (0, import_jsx_runtime7.jsxs)("div", { style: { display: "flex", gap: "var(--w-space-3)", alignItems: "flex-start" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(Well, { width: 128, height: 218, caption: "\u70B9\u4E00\u4E2A\u5305\u5F00\u59CB\u9884\u89C8", radius: "12px", style: { flexShrink: 0 } }),
          /* @__PURE__ */ (0, import_jsx_runtime7.jsxs)("div", { style: { flex: 1, minWidth: 0, display: "flex", flexDirection: "column", gap: "var(--w-space-3)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(PickerPlaceholder, { title: "\u670D\u88C5" }),
            /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(PickerPlaceholder, { title: "\u8868\u60C5" }),
            /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(PickerPlaceholder, { title: "\u52A8\u4F5C" }),
            /* @__PURE__ */ (0, import_jsx_runtime7.jsx)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-2)" }, children: /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(PrimarySm, { disabled: true, children: "\u64AD\u653E" }) })
          ] })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(Cap, { children: "\u52A8\u4F5C\u9884\u89C8\u53EA\u8BFB\u5F53\u524D\u5305\uFF1A\u8D70\u8DEF\u52A8\u4F5C\u6309\u4F4D\u79FB\u7B97\u76F8\u4F4D\uFF0C\u548C\u4E3B\u754C\u9762\u5171\u7528\u540C\u4E00\u5957 WalkCycle\uFF1B\u9875\u9762\u79BB\u5F00\u4F1A\u81EA\u52A8\u505C\u3002" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime7.jsxs)(Card, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(Sub, { children: "AI \u7740\u88C5\u7D20\u6750" }),
        /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(Cap, { children: "\u751F\u56FE\u6A21\u578B\u753B\u4E00\u5F20\u90E8\u4EF6\u7D20\u6750\u8868 \u2192 \u8FD9\u91CC\u6309\u7F51\u683C\u5207\u5F00\u5E76\u5199\u5165\u5305 \u2192 \u52A8\u753B\u7531\u5916\u90E8\u6309\u5E27\u6E32\u67D3\u6216\u5F55\u6210\u900F\u660E\u89C6\u9891 \u2192 \u56DE\u5230\u8FD9\u91CC\u5BFC\u5165\u52A8\u4F5C\u3002\u6CA1\u6709\u5F00\u542F\u753B\u9762\u751F\u6210\u65F6\u4E0D\u4F1A\u5047\u88C5\u6210\u529F\u3002" }),
        /* @__PURE__ */ (0, import_jsx_runtime7.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime7.jsxs)("div", { children: [
            /* @__PURE__ */ (0, import_jsx_runtime7.jsx)("div", { style: FIELD_LABEL, children: "\u5305 id" }),
            /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(
              "input",
              {
                className: "k-field",
                style: FIELD,
                value: packId,
                placeholder: "figure_maid",
                "aria-label": "\u5305 id",
                onChange: (event) => setPackId(event.target.value)
              }
            )
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime7.jsxs)("div", { children: [
            /* @__PURE__ */ (0, import_jsx_runtime7.jsx)("div", { style: FIELD_LABEL, children: "\u663E\u793A\u540D\uFF08\u53EF\u7A7A\uFF09" }),
            /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(
              "input",
              {
                className: "k-field",
                style: FIELD,
                value: name,
                placeholder: "\u65B0\u5F62\u8C61",
                "aria-label": "\u663E\u793A\u540D\uFF08\u53EF\u7A7A\uFF09",
                onChange: (event) => setName(event.target.value)
              }
            )
          ] })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(
          "textarea",
          {
            className: "k-field k-field--area",
            style: { ...FIELD, minHeight: 64, height: 64 },
            value: prompt,
            placeholder: "\u4F8B\uFF1A\u540C\u4E00\u4F4D\u5C11\u5973\u7684\u5934\u53D1\u3001\u4E0A\u8863\u3001\u88D9\u5B50\u3001\u978B\u5B50\u5206\u4EF6\uFF0C\u900F\u660E\u80CC\u666F",
            "aria-label": "\u751F\u6210\u63D0\u793A\u8BCD",
            onChange: (event) => setPrompt(event.target.value)
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime7.jsxs)("div", { style: { display: "flex", alignItems: "flex-end", gap: "var(--w-space-2)", flexWrap: "wrap" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime7.jsxs)("div", { children: [
            /* @__PURE__ */ (0, import_jsx_runtime7.jsx)("div", { style: FIELD_LABEL, children: "\u884C" }),
            /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(
              "input",
              {
                className: "k-field",
                style: { ...FIELD, width: 58 },
                value: rows2,
                inputMode: "numeric",
                "aria-label": "\u884C",
                onChange: (event) => setRows(event.target.value)
              }
            )
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime7.jsxs)("div", { children: [
            /* @__PURE__ */ (0, import_jsx_runtime7.jsx)("div", { style: FIELD_LABEL, children: "\u5217" }),
            /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(
              "input",
              {
                className: "k-field",
                style: { ...FIELD, width: 58 },
                value: columns,
                inputMode: "numeric",
                "aria-label": "\u5217",
                onChange: (event) => setColumns(event.target.value)
              }
            )
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime7.jsxs)("div", { children: [
            /* @__PURE__ */ (0, import_jsx_runtime7.jsx)("div", { style: FIELD_LABEL, children: "\u69FD\u4F4D" }),
            /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(
              Select,
              {
                label: "\u69FD\u4F4D",
                value: slot,
                options: SLOTS.map((item) => ({ value: item, label: item })),
                onChange: setSlot
              }
            )
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime7.jsx)("div", { style: { marginLeft: "auto" }, children: /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(PrimarySm, { children: "\u751F\u6210\u5E76\u5207\u56FE" }) })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(Sep, {}),
        /* @__PURE__ */ (0, import_jsx_runtime7.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr auto", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime7.jsxs)("div", { children: [
            /* @__PURE__ */ (0, import_jsx_runtime7.jsx)("div", { style: FIELD_LABEL, children: "\u52A8\u4F5C id" }),
            /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(
              "input",
              {
                className: "k-field",
                style: FIELD,
                value: motionId,
                placeholder: "walk",
                "aria-label": "\u52A8\u4F5C id",
                onChange: (event) => setMotionId(event.target.value)
              }
            )
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime7.jsxs)("div", { children: [
            /* @__PURE__ */ (0, import_jsx_runtime7.jsx)("div", { style: FIELD_LABEL, children: "\u53D6\u6837\u5E27\u7387" }),
            /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(
              "input",
              {
                className: "k-field",
                style: { ...FIELD, width: 72 },
                value: motionFps,
                inputMode: "numeric",
                "aria-label": "\u53D6\u6837\u5E27\u7387",
                onChange: (event) => setMotionFps(event.target.value)
              }
            )
          ] })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(Cap, { children: "\u628A\u900F\u660E\u80CC\u666F\u89C6\u9891\u6216\u6309\u987A\u5E8F\u6392\u597D\u7684 PNG \u5E27\u5F55\u6210\u52A8\u4F5C\u3002\u89C6\u9891\u62BD\u5E27\u9700\u8981\u672C\u673A ffmpeg\uFF1B\u6CA1\u6709\u65F6\u8BF7\u6539\u7528\u5E27\u5E8F\u5217\u3002" }),
        /* @__PURE__ */ (0, import_jsx_runtime7.jsxs)("div", { style: { display: "flex", flexWrap: "wrap", gap: 6 }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(GhostSm, { children: "\u5BFC\u5165\u5E27\u5E8F\u5217\u2026" }),
          /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(GhostSm, { children: "\u5BFC\u5165\u900F\u660E\u89C6\u9891\u2026" })
        ] })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime7.jsxs)(Card, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(Sub, { children: "\u5BFC\u5165\u5F62\u8C61\u5305" }),
        /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(Cap, { children: "\u9009\u4E00\u4E2A zip\u3002\u538B\u7F29\u5305\u6839\u76EE\u5F55\uFF08\u6216\u552F\u4E00\u7684\u4E00\u5C42\u6587\u4EF6\u5939\uFF09\u91CC\u8981\u6709 figure.json\uFF0C\u8D34\u56FE\u8DEF\u5F84\u6309\u5305\u5185\u76F8\u5BF9\u8DEF\u5F84\u89E3\u6790\u3002" }),
        /* @__PURE__ */ (0, import_jsx_runtime7.jsx)("div", { children: /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(PrimarySm, { children: "\u9009\u62E9 zip \u5BFC\u5165" }) })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(Cap, { style: { color: "var(--w-signal)" }, children: "\u8FD8\u6CA1\u6709\u5F62\u8C61\u5305\u3002\u628A zip \u62D6\u8FDB\u6765\uFF08\u6216\u70B9\u4E0A\u9762\u7684\u6309\u94AE\uFF09\u5C31\u80FD\u5F00\u59CB\u7528\u65B0\u7248\u7740\u88C5\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime7.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-3)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime7.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(Sub, { children: "\u5DF2\u5B89\u88C5" }),
          /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(Cap, { children: "\u5171 0 \u4E2A" })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(Card, { tight: true, children: /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(NoteRow, { children: "\u8FD8\u6CA1\u6709\u5F62\u8C61\u5305\u3002\u5BFC\u5165\u4E00\u4E2A zip \u5C31\u80FD\u5F00\u59CB\u7528\u65B0\u7248\u7740\u88C5\u3002" }) })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(Cap, { children: "\u70B9\u4EFB\u610F\u4E00\u884C\u9009\u4E2D\u5B83\uFF1A\u4E0A\u9762\u4F1A\u663E\u793A\u8FD9\u4E00\u5305\u7684\u670D\u88C5\u3001\u8868\u60C5\u4E0E\u52A8\u4F5C\uFF0C\u7ED1\u5B9A/\u89E3\u7ED1\u4ECD\u53EA\u4F5C\u7528\u4E8E\u300C\u7ED1\u7ED9\u8C01\u300D\u91CC\u9009\u4E2D\u7684\u89D2\u8272\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime7.jsx)(Cap, { children: "\u5F62\u8C61\u5305\u7531\u90E8\u4EF6\u3001\u670D\u88C5\u3001\u914D\u8272\u4E0E\u52A8\u4F5C\u7EC4\u6210\uFF1B\u7F3A\u56FE\u6216\u5305\u635F\u574F\u65F6\u4F1A\u81EA\u52A8\u56DE\u9000\u5230\u65E7\u7248\u7ACB\u7ED8\uFF0C\u4E0D\u4F1A\u628A\u89D2\u8272\u53D8\u7A7A\u767D\u3002" })
    ] })
  ] });
}

// src/screens/character-media/materials.tsx
var import_react5 = __toESM(require_react(), 1);
var import_jsx_runtime8 = __toESM(require_jsx_runtime(), 1);
var TAB_BASE = {
  minHeight: 34,
  padding: "10px 6px",
  border: 0,
  borderRadius: "var(--w-radius-button)",
  font: "inherit",
  fontSize: "var(--w-size-small)",
  cursor: "pointer"
};
function Materials({ back }) {
  const [tab, setTab] = (0, import_react5.useState)("backgrounds");
  const isMusic = tab === "music";
  return /* @__PURE__ */ (0, import_jsx_runtime8.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime8.jsx)(
      PageHeader,
      {
        title: "\u7D20\u6750\u5E93",
        onBack: back,
        right: /* @__PURE__ */ (0, import_jsx_runtime8.jsx)(
          "span",
          {
            style: {
              padding: "5px 10px",
              borderRadius: "99px",
              background: "var(--w-surface-tint)",
              fontSize: "var(--w-size-small)",
              color: "var(--w-ink-display)"
            },
            children: "\u80CC\u666F \xB7 \u97F3\u4E50"
          }
        )
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime8.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--w-space-2)", padding: "16px var(--w-pad-card)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime8.jsx)(
        "button",
        {
          type: "button",
          "aria-pressed": !isMusic,
          onClick: () => setTab("backgrounds"),
          style: {
            ...TAB_BASE,
            background: isMusic ? "transparent" : "var(--w-surface-tint)",
            color: isMusic ? "var(--w-ink-body)" : "var(--w-ink-display)",
            fontWeight: isMusic ? 400 : 700
          },
          children: "\u80CC\u666F\u5E93"
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime8.jsx)(
        "button",
        {
          type: "button",
          "aria-pressed": isMusic,
          onClick: () => setTab("music"),
          style: {
            ...TAB_BASE,
            background: isMusic ? "var(--w-surface-tint)" : "transparent",
            color: isMusic ? "var(--w-ink-display)" : "var(--w-ink-body)",
            fontWeight: isMusic ? 700 : 400
          },
          children: "\u97F3\u4E50\u5E93"
        }
      )
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime8.jsx)("div", { className: "screen", children: /* @__PURE__ */ (0, import_jsx_runtime8.jsxs)(
      "div",
      {
        style: {
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          justifyContent: "center",
          gap: "var(--w-space-3)",
          padding: 32,
          minHeight: 320
        },
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime8.jsx)(Well, { width: 96, height: 96, radius: "99px" }),
          /* @__PURE__ */ (0, import_jsx_runtime8.jsx)("h2", { style: { margin: 0, fontSize: "var(--w-size-title)", fontWeight: 600, color: "var(--w-ink-display)" }, children: isMusic ? "\u97F3\u4E50\u5E93\u8FD8\u662F\u7A7A\u7684" : "\u80CC\u666F\u5E93\u8FD8\u662F\u7A7A\u7684" }),
          isMusic ? /* @__PURE__ */ (0, import_jsx_runtime8.jsx)("span", { style: { fontSize: "var(--w-size-caption)", color: "var(--w-ink-faint)" }, children: "\u5BFC\u5165\u97F3\u4E50\u540E\u53EF\u8BBE\u4E3A\u5DE6\u952E\u97F3/\u53F3\u952E\u97F3/\u6309\u952E\u97F3/\u80CC\u666F\u97F3" }) : /* @__PURE__ */ (0, import_jsx_runtime8.jsx)("span", { style: { fontSize: "var(--w-size-caption)", color: "var(--w-ink-faint)" }, children: "\u5BFC\u5165 JPG / PNG / MP4 \u80CC\u666F\uFF0C\u53EF\u5728\u5730\u56FE\u91CC\u9009\u7528" })
        ]
      }
    ) }),
    /* @__PURE__ */ (0, import_jsx_runtime8.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--w-space-3)", padding: "var(--w-pad-card)" }, children: [
      isMusic ? /* @__PURE__ */ (0, import_jsx_runtime8.jsx)("span", {}) : /* @__PURE__ */ (0, import_jsx_runtime8.jsx)(PrimarySm, { children: "\u5BFC\u5165\u80CC\u666F" }),
      isMusic ? /* @__PURE__ */ (0, import_jsx_runtime8.jsx)(PrimarySm, { children: "\u5BFC\u5165\u97F3\u4E50" }) : /* @__PURE__ */ (0, import_jsx_runtime8.jsx)("span", {})
    ] })
  ] });
}

// src/screens/character-media/portraitCalibration.tsx
var import_react6 = __toESM(require_react(), 1);
var import_jsx_runtime9 = __toESM(require_jsx_runtime(), 1);
var FRAME_W = 270;
var FRAME_H = 480;
var SRC_W = 400;
var SRC_H = 640;
var DEFAULT_ANCHOR_Y = 0.9;
var round = (value) => Math.round(value * 100) / 100;
function PortraitCalibration({ back }) {
  const [flipH, setFlipH] = (0, import_react6.useState)(false);
  const [flipV, setFlipV] = (0, import_react6.useState)(false);
  const [rotation, setRotation] = (0, import_react6.useState)(0);
  const [scale, setScale] = (0, import_react6.useState)(1);
  const [offsetX, setOffsetX] = (0, import_react6.useState)(0);
  const [offsetY, setOffsetY] = (0, import_react6.useState)(0);
  const [footLine, setFootLine] = (0, import_react6.useState)(1);
  const swap = rotation % 180 !== 0;
  const effW = swap ? SRC_H : SRC_W;
  const effH = swap ? SRC_W : SRC_H;
  const fit = Math.min(FRAME_W / effW, FRAME_H / effH);
  const baseW = round(effW * fit);
  const baseH = round(effH * fit);
  const finalScale = Math.min(8, Math.max(0.02, scale));
  const anchorX = FRAME_W / 2;
  const anchorY = DEFAULT_ANCHOR_Y * FRAME_H;
  const previewX = round(anchorX - FRAME_W / 2 + offsetX * FRAME_W);
  const previewY = round(
    anchorY - FRAME_H / 2 - baseH * finalScale / 2 + baseH * finalScale * (1 - footLine) + offsetY * FRAME_H
  );
  const groundLineY = round(anchorY);
  const status = `\u6709\u6548\u5C3A\u5BF8 ${effW}\xD7${effH} \xB7 \u663E\u793A ${Math.round(baseW * finalScale)}\xD7${Math.round(
    baseH * finalScale
  )} \xB7 \u7F29\u653E ${finalScale.toFixed(2)}`;
  const reset = () => {
    setFlipH(false);
    setFlipV(false);
    setRotation(0);
    setScale(1);
    setOffsetX(0);
    setOffsetY(0);
    setFootLine(1);
  };
  return /* @__PURE__ */ (0, import_jsx_runtime9.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(PageHeader, { title: "\u7ACB\u7ED8\u6807\u5B9A", onBack: back }),
    /* @__PURE__ */ (0, import_jsx_runtime9.jsxs)("div", { className: "screen", style: { maxWidth: 760, gap: "var(--w-space-3)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(Cap, { children: "\u7ACB\u7ED8\u6309 1080\xD71920 \u7684\u903B\u8F91\u753B\u5E03\u5BF9\u9F50\u3002\u5141\u8BB8\u89D2\u8272\u6EA2\u51FA\u753B\u5E03\uFF0C\u4F46\u811A\u5E95\u5FC5\u987B\u843D\u5728\u8FD9\u5757\u753B\u5E03\u7684\u4E0B\u6CBF\u7EBF\u4E0A\uFF0C\u5927\u5C0F\u4E0E\u671D\u5411\u4E5F\u8981\u6B63\u786E\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime9.jsxs)(
        "div",
        {
          style: {
            position: "relative",
            width: FRAME_W,
            height: FRAME_H,
            margin: "0 auto",
            overflow: "hidden",
            borderRadius: "var(--w-radius-well)",
            border: "1px solid var(--w-hairline)",
            background: "var(--w-bg)"
          },
          children: [
            /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(
              "div",
              {
                style: {
                  position: "absolute",
                  left: "50%",
                  top: "50%",
                  width: baseW,
                  height: baseH,
                  marginLeft: -baseW / 2,
                  marginTop: -baseH / 2,
                  transform: `translate(${previewX}px, ${previewY}px) rotate(${rotation}deg) scale(${(flipH ? -1 : 1) * finalScale}, ${(flipV ? -1 : 1) * finalScale})`,
                  border: "1px solid var(--w-hairline)",
                  boxSizing: "border-box",
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "center"
                },
                children: /* @__PURE__ */ (0, import_jsx_runtime9.jsx)("span", { style: { fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)" }, children: "\u7ACB\u7ED8" })
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(
              "div",
              {
                style: {
                  position: "absolute",
                  left: 0,
                  right: 0,
                  top: groundLineY,
                  height: 1.5,
                  background: "var(--w-signal)",
                  opacity: 0.9
                }
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(
              "span",
              {
                style: {
                  position: "absolute",
                  right: 4,
                  top: groundLineY,
                  transform: "translateY(-12px)",
                  padding: "2px 6px",
                  borderRadius: "var(--w-radius-well)",
                  background: "var(--w-surface-tint)",
                  fontSize: "var(--w-size-caption)",
                  color: "var(--w-ink-muted)"
                },
                children: "\u811A\u5E95\u57FA\u51C6\u7EBF"
              }
            )
          ]
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime9.jsxs)(Card, { tight: true, children: [
        /* @__PURE__ */ (0, import_jsx_runtime9.jsx)("div", { style: { padding: "4px 0", fontSize: "var(--w-size-small)", color: "var(--w-ink-body)" }, children: "\u7D20\u6750\u5C3A\u5BF8\uFF1A\u8BFB\u53D6\u4E2D\u2026" }),
        /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(Sep, { bleed: true }),
        /* @__PURE__ */ (0, import_jsx_runtime9.jsx)("div", { style: { padding: "4px 0", fontSize: "var(--w-size-small)", color: "var(--w-ink-body)" }, children: status }),
        /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(Sep, { bleed: true }),
        /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(Cap, { children: "\u5F53\u524D\u573A\u666F\u6CA1\u6709\u53EF\u7528\u6807\u5B9A\uFF0C\u6309\u6E38\u620F\u9ED8\u8BA4\u5C45\u4E2D + \u843D\u811A\u70B9 0.9\uFF08\u753B\u9762 90% \u9AD8\u5EA6\u5904\uFF09" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(Card, { tight: true, children: /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(
        RowLine,
        {
          title: "\u65B9\u5411",
          right: /* @__PURE__ */ (0, import_jsx_runtime9.jsxs)("div", { style: { display: "flex", gap: "var(--w-space-2)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(GhostSm, { onClick: () => setFlipH((v) => !v), children: "\u6C34\u5E73\u7FFB\u8F6C" }),
            /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(GhostSm, { onClick: () => setFlipV((v) => !v), children: "\u5782\u76F4\u7FFB\u8F6C" }),
            /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(GhostSm, { onClick: () => setRotation((v) => (v + 90) % 360), children: "\u65CB\u8F6C 90\xB0" })
          ] })
        }
      ) }),
      /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(Card, { tight: true, children: /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(
        RowLine,
        {
          title: "\u5927\u5C0F\uFF08>100% \u5373\u6EA2\u51FA\u753B\u5E03\uFF09",
          right: /* @__PURE__ */ (0, import_jsx_runtime9.jsxs)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-2)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(GhostSm, { width: 80, onClick: () => setScale((v) => Math.max(0.1, Math.round((v - 0.05) * 100) / 100)), children: "\u7F29\u5C0F" }),
            /* @__PURE__ */ (0, import_jsx_runtime9.jsxs)(
              "span",
              {
                style: {
                  width: 56,
                  textAlign: "center",
                  fontSize: "var(--w-size-small)",
                  color: "var(--w-ink-body)",
                  fontVariantNumeric: "tabular-nums"
                },
                children: [
                  Math.round(scale * 100),
                  "%"
                ]
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(GhostSm, { width: 80, onClick: () => setScale((v) => Math.min(4, Math.round((v + 0.05) * 100) / 100)), children: "\u653E\u5927" })
          ] })
        }
      ) }),
      /* @__PURE__ */ (0, import_jsx_runtime9.jsxs)(Card, { tight: true, children: [
        /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(
          RowLine,
          {
            title: "\u4F4D\u7F6E\u4E0E\u811A\u5E95\u7EBF",
            right: /* @__PURE__ */ (0, import_jsx_runtime9.jsxs)("div", { style: { display: "flex", gap: "var(--w-space-2)" }, children: [
              /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(GhostSm, { width: 52, onClick: () => setOffsetX((v) => round(v - 0.01)), children: "\u2190" }),
              /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(GhostSm, { width: 52, onClick: () => setOffsetX((v) => round(v + 0.01)), children: "\u2192" }),
              /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(GhostSm, { width: 52, onClick: () => setOffsetY((v) => round(v - 0.01)), children: "\u2191" }),
              /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(GhostSm, { width: 52, onClick: () => setOffsetY((v) => round(v + 0.01)), children: "\u2193" })
            ] })
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(Sep, {}),
        /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(
          RowLine,
          {
            title: "\u811A\u5E95\u7EBF",
            right: /* @__PURE__ */ (0, import_jsx_runtime9.jsxs)("div", { style: { display: "flex", gap: "var(--w-space-2)" }, children: [
              /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(GhostSm, { width: 96, onClick: () => setFootLine((v) => round(Math.max(0.5, v - 0.01))), children: "\u62AC\u811A\u7EBF\u4E0A\u79FB" }),
              /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(GhostSm, { width: 96, onClick: () => setFootLine((v) => round(Math.min(1, v + 0.01))), children: "\u56DE\u5230\u5E95\u8FB9" })
            ] })
          }
        )
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime9.jsxs)("div", { style: { display: "flex", gap: "var(--w-space-2)", justifyContent: "center" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(GhostSm, { onClick: reset, children: "\u91CD\u7F6E" }),
        /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(GhostSm, { onClick: reset, style: { color: "var(--w-signal)" }, children: "\u6E05\u9664\u6807\u5B9A" }),
        /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(PrimarySm, { disabled: true, children: "\u4FDD\u5B58" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime9.jsxs)(Card, { tight: true, children: [
        /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(
          RowLine,
          {
            title: /* @__PURE__ */ (0, import_jsx_runtime9.jsxs)("span", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-1)" }, children: [
              /* @__PURE__ */ (0, import_jsx_runtime9.jsx)("span", { children: "\u89C4\u8303\u5316\uFF08\u53EF\u9009\uFF09" }),
              /* @__PURE__ */ (0, import_jsx_runtime9.jsx)("span", { className: "k-row__desc", children: "\u6807\u5B9A\u53EA\u8BB0\u300C\u600E\u4E48\u6620\u5C04\u300D\uFF0C\u4E0D\u6539\u6587\u4EF6\uFF1B\u89C4\u8303\u5316\u4F1A\u6309\u5F53\u524D\u7684\u7FFB\u8F6C\u4E0E\u65CB\u8F6C\uFF0C\u771F\u7684\u751F\u6210\u4E00\u5F20 1080\xD71920 \u7684 PNG\uFF08\u7B49\u6BD4\u5C45\u4E2D\u3001\u4F59\u91CF\u900F\u660E\uFF09\uFF0C\u5E76\u628A\u89D2\u8272\u7684\u7ACB\u7ED8\u6307\u5411\u5B83\u3002" }),
              /* @__PURE__ */ (0, import_jsx_runtime9.jsx)("span", { className: "k-row__desc", children: "\u4F59\u91CF\u7559\u900F\u660E\u4F1A\u8BA9\u89D2\u8272\u7684\u663E\u793A\u5360\u6BD4\u53D8\u5C0F\u2014\u2014\u7D20\u6750\u6BD4\u4F8B\u8D8A\u63A5\u8FD1 9:16\uFF0C\u900F\u660E\u8FB9\u8D8A\u5C11\u3001\u8D8A\u8D34\u5408\u3002" })
            ] }),
            right: /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(GhostSm, { children: "\u751F\u6210 1080\xD71920 \u6807\u51C6\u753B\u5E03\u6587\u4EF6" })
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime9.jsx)(Sep, {})
      ] })
    ] })
  ] });
}

// src/screens/character-media/avatarcrop.tsx
var import_react7 = __toESM(require_react(), 1);
var import_jsx_runtime10 = __toESM(require_jsx_runtime(), 1);
var RULE = { position: "absolute", background: "var(--w-hairline)" };
function Avatarcrop({ back }) {
  const [showGrid, setShowGrid] = (0, import_react7.useState)(true);
  return /* @__PURE__ */ (0, import_jsx_runtime10.jsx)(PageShell, { children: /* @__PURE__ */ (0, import_jsx_runtime10.jsxs)("div", { style: { height: "100%", display: "flex", flexDirection: "column" }, children: [
    /* @__PURE__ */ (0, import_jsx_runtime10.jsx)(PageHeader, { title: "\u88C1\u526A\u5934\u50CF", right: /* @__PURE__ */ (0, import_jsx_runtime10.jsx)(GhostSm, { onClick: () => setShowGrid(true), children: "\u21BA \u91CD\u7F6E" }) }),
    /* @__PURE__ */ (0, import_jsx_runtime10.jsxs)("div", { style: { position: "relative", flex: 1, minHeight: 420, background: "var(--w-bg)", overflow: "hidden" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime10.jsx)("div", { style: { position: "absolute", inset: 0, background: "var(--w-surface-tint)", opacity: 0.4 } }),
      /* @__PURE__ */ (0, import_jsx_runtime10.jsxs)(
        "div",
        {
          style: {
            position: "absolute",
            left: "50%",
            top: "50%",
            transform: "translate(-50%, -50%)",
            width: "min(300px, calc(100% - 32px))",
            aspectRatio: "1",
            overflow: "hidden",
            background: "var(--w-bg)",
            border: "1px solid var(--w-hairline)",
            borderRadius: 2,
            display: "flex",
            alignItems: "center",
            justifyContent: "center"
          },
          children: [
            /* @__PURE__ */ (0, import_jsx_runtime10.jsx)("span", { style: { fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)" }, children: "\u5934\u50CF" }),
            showGrid ? /* @__PURE__ */ (0, import_jsx_runtime10.jsxs)("div", { style: { position: "absolute", inset: 0, pointerEvents: "none" }, "aria-hidden": "true", children: [
              /* @__PURE__ */ (0, import_jsx_runtime10.jsx)("i", { style: { ...RULE, left: "33.33%", top: 0, bottom: 0, width: 1 } }),
              /* @__PURE__ */ (0, import_jsx_runtime10.jsx)("i", { style: { ...RULE, left: "66.66%", top: 0, bottom: 0, width: 1 } }),
              /* @__PURE__ */ (0, import_jsx_runtime10.jsx)("i", { style: { ...RULE, top: "33.33%", left: 0, right: 0, height: 1 } }),
              /* @__PURE__ */ (0, import_jsx_runtime10.jsx)("i", { style: { ...RULE, top: "66.66%", left: 0, right: 0, height: 1 } })
            ] }) : null
          ]
        }
      )
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime10.jsxs)("div", { style: { padding: "10px 20px 4px", display: "flex", alignItems: "center", gap: "var(--w-space-2)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime10.jsx)(GhostSm, { onClick: () => setShowGrid((value) => !value), style: { height: 32 }, children: showGrid ? "\u9690\u85CF\u7F51\u683C" : "\u663E\u793A\u7F51\u683C" }),
      /* @__PURE__ */ (0, import_jsx_runtime10.jsx)("span", { style: { flex: 1, textAlign: "center", fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)" }, children: "\u62D6\u52A8\u53D6\u666F \xB7 \u6EDA\u8F6E / \u53CC\u6307\u7F29\u653E" })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime10.jsxs)(
      "div",
      {
        style: {
          display: "grid",
          gridTemplateColumns: "auto 1fr auto",
          alignItems: "center",
          gap: "var(--w-space-3)",
          padding: "10px 16px 14px",
          background: "var(--w-surface-tint)"
        },
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime10.jsx)(TextSm, { onClick: back, children: "\u53D6\u6D88" }),
          /* @__PURE__ */ (0, import_jsx_runtime10.jsx)(
            "span",
            {
              style: {
                textAlign: "center",
                fontSize: "var(--w-size-caption)",
                color: "var(--w-ink-muted)",
                overflow: "hidden",
                textOverflow: "ellipsis",
                whiteSpace: "nowrap"
              },
              children: "TA"
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime10.jsx)(PrimarySm, { disabled: true, children: "\u786E\u8BA4" })
        ]
      }
    )
  ] }) });
}

// src/screens/character-media/memoryManage.tsx
var import_jsx_runtime11 = __toESM(require_jsx_runtime(), 1);
var LEGEND = ["\u4EBA", "\u8BDD\u9898", "\u4E8B", "\u5730\u70B9", "\u7269"];
function MemoryManage({ navigate, back }) {
  return /* @__PURE__ */ (0, import_jsx_runtime11.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime11.jsx)(
      PageHeader,
      {
        title: "\u8BB0\u5FC6\u7BA1\u7406",
        subtitle: "\u8FD9\u91CC\u80FD\u770B\u5230\u5E76\u4FEE\u6539 TA \u7684\u8BB0\u5FC6\uFF1A\u5173\u7CFB\u7F51\u3001\u6807\u7B7E\u3001TA \u8BB0\u5F97\u7684\u4E8B\uFF0C\u4EE5\u53CA\u4E3B\u8BBE\u5B9A \xD7 \u526F\u8BBE\u5B9A\u3002",
        onBack: back
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime11.jsxs)("div", { className: "screen", style: { gap: "var(--w-space-3)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime11.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime11.jsx)(Cap, { children: "\u8FD9\u4E9B\u8BB0\u5FC6\uFF1A\u53EA\u5728\u8FD9\u4EFD\u5B58\u6863\u91CC\uFF08\u5F00\u5173\u5728\u8BBE\u7F6E \u2192 \u8BB0\u5FC6\u91CC\uFF09" }),
        /* @__PURE__ */ (0, import_jsx_runtime11.jsx)(TextSm, { children: "\u5237\u65B0" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime11.jsxs)(Card, { tight: true, children: [
        /* @__PURE__ */ (0, import_jsx_runtime11.jsx)(
          RowLine,
          {
            title: "\u526F\u8BBE\u5B9A\uFF08\u5F53\u4E0B\u4E4B\u6211\uFF09",
            desc: "\u4E3B\u8BBE\u5B9A\u4E0D\u53EF\u6539\uFF1B\u526F\u8BBE\u5B9A\u968F\u7ECF\u5386\u6F02\u79FB\uFF0C\u53EF\u4EE5\u770B\u5BF9\u6BD4\u3001\u4E5F\u53EF\u4EE5\u56DE\u6B63\u5230\u4E3B\u8BBE\u5B9A\u3002",
            right: /* @__PURE__ */ (0, import_jsx_runtime11.jsx)(GhostSm, { children: "\u6253\u5F00\u4E3B\u8BBE\u5B9A \xD7 \u526F\u8BBE\u5B9A\u5BF9\u6BD4" })
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime11.jsx)(Sep, {}),
        /* @__PURE__ */ (0, import_jsx_runtime11.jsx)(
          RowLine,
          {
            title: "\u5173\u7CFB\u7F51\u4E0E\u641C\u4E8B\u4EF6\uFF08\u5404\u5F00\u4E00\u6247\u7A97\uFF09",
            desc: "\u8BCD\u3001\u4EBA\u3001\u5730\u70B9\u3001\u6807\u7B7E\u3001\u7167\u7247\u3001\u8BB0\u5F97\u7684\u4E8B\u90FD\u5728\u540C\u4E00\u5F20\u7F51\u4E0A\uFF1A\u770B\u5F97\u89C1\u5B83\u957F\u51FA\u6765\uFF0C\u4E5F\u80FD\u5F53\u573A\u6539 \u2014\u2014 \u8BBE\u7B49\u4EF7\u3001\u52A0\u8054\u7CFB\u3001\u65AD\u5F00\u3001\u5FD8\u6389\u3002\u641C\u4E8B\u4EF6\u6309\u4E00\u53E5\u8BDD\u3001\u6807\u7B7E\u3001\u90A3\u79CD\u611F\u89C9\u3001\u6216\u8005\u90A3\u5929\u7684\u65E5\u5B50\u627E\u3002",
            right: /* @__PURE__ */ (0, import_jsx_runtime11.jsxs)("span", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)", alignItems: "flex-end" }, children: [
              /* @__PURE__ */ (0, import_jsx_runtime11.jsx)(GhostSm, { onClick: () => navigate("/memoryGraph"), children: "\u6253\u5F00\u5173\u7CFB\u7F51" }),
              /* @__PURE__ */ (0, import_jsx_runtime11.jsx)(GhostSm, { onClick: () => navigate("/memorySearch"), children: "\u641C\u4E8B\u4EF6" })
            ] })
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime11.jsx)(Sep, {}),
        /* @__PURE__ */ (0, import_jsx_runtime11.jsx)(
          RowLine,
          {
            title: "\u6807\u7B7E\uFF08TA \u600E\u4E48\u8BB0\u4EBA\u548C\u4E8B\uFF09",
            desc: "\u8FD8\u6CA1\u6709\u6807\u7B7E\u3002\u53EF\u4EE5\u5728\u8FD9\u91CC\u624B\u5DE5\u52A0\uFF0CAI \u5728\u5BF9\u8BDD\u91CC\u4E5F\u4F1A\u81EA\u5DF1\u6253\u3002",
            right: /* @__PURE__ */ (0, import_jsx_runtime11.jsxs)("span", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)", alignItems: "flex-end" }, children: [
              /* @__PURE__ */ (0, import_jsx_runtime11.jsx)(GhostSm, { children: "\u52A0\u6807\u7B7E" }),
              /* @__PURE__ */ (0, import_jsx_runtime11.jsx)(GhostSm, { children: "\u5220\u6807\u7B7E" })
            ] })
          }
        )
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime11.jsxs)("div", { children: [
        /* @__PURE__ */ (0, import_jsx_runtime11.jsx)(SectionTitle, { children: "TA \u8BB0\u5F97\u7684\u4E8B\uFF08\u4EBA\u7C7B\u8BB0\u5FC6\u5C42\uFF09" }),
        /* @__PURE__ */ (0, import_jsx_runtime11.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 6, marginBottom: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime11.jsx)(Muted, { children: "\u53EA\u8BB0\u4E8B\u4EF6\u3001\u5370\u8C61\u3001\u91CD\u8981\u65F6\u523B\u4E0E\u5355\u5F20\u7167\u7247\uFF1B\u8D8A\u91CD\u8981\u5FD8\u5F97\u8D8A\u6162\uFF0C\u4E45\u4E86\u7684\u4F1A\u6A21\u7CCA\u3002" }),
          /* @__PURE__ */ (0, import_jsx_runtime11.jsx)(Muted, { children: "\u70B9\u4E86\u6807\u7B7E\u7684\u53E5\u5B50\u4F1A\u94FA\u4E0A\u5E95\u8272\uFF1A\u989C\u8272 = \u8FD9\u53E5\u8BB0\u7684\u662F\u4EBA / \u8BDD\u9898 / \u4E8B / \u5730\u70B9 / \u7269\u3002\u60F3\u6539\u5C31\u70B9\u90A3\u53E5\u3002" }),
          /* @__PURE__ */ (0, import_jsx_runtime11.jsx)(Muted, { children: "\u6838\u5FC3\u8BB0\u5FC6\u662F\u4EBA\u683C\u7684\u951A\u70B9\uFF1A\u6C38\u4E0D\u9057\u5FD8\u3001\u6BCF\u6B21\u90FD\u4F1A\u88AB\u60F3\u8D77\uFF0C\u8FD8\u4F1A\u5F71\u54CD\u5468\u56F4\u7684\u8BB0\u5FC6\uFF1B\u521B\u4F24\u662F\u7279\u6B8A\u7684\u6838\u5FC3\u8BB0\u5FC6\uFF0C\u53EA\u4F1A\u968F\u5177\u4F53\u4E92\u52A8\u6162\u6162\u964D\u6E29\uFF0C\u5185\u5BB9\u4E0D\u4F1A\u6D88\u5931\u3002" })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime11.jsx)("div", { style: { display: "flex", flexWrap: "wrap", gap: "var(--w-space-1)", marginBottom: "var(--w-space-2)" }, children: LEGEND.map((label) => /* @__PURE__ */ (0, import_jsx_runtime11.jsx)(
          "span",
          {
            style: {
              padding: "2px 8px",
              borderRadius: 6,
              border: "1px solid var(--w-hairline)",
              background: "var(--w-surface-tint)",
              fontSize: "var(--w-size-caption)",
              color: "var(--w-ink-display)"
            },
            children: label
          },
          label
        )) }),
        /* @__PURE__ */ (0, import_jsx_runtime11.jsx)(GhostSm, { children: "\uFF0B \u624B\u52A8\u52A0\u4E00\u6761\u8BB0\u5FC6" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime11.jsx)(Cap, { children: "\u8FD8\u6CA1\u6709\u8BB0\u5F97\u7684\u4E8B\u2014\u2014\u591A\u804A\u51E0\u53E5\uFF0C\u91CD\u8981\u7684\u4F1A\u88AB\u8BB0\u4E0B\u6765\u3002" })
    ] })
  ] });
}

// src/screens/character-media/memoryGraph.tsx
var import_react8 = __toESM(require_react(), 1);
var import_jsx_runtime12 = __toESM(require_jsx_runtime(), 1);
var KIND_TEXT = {
  person: "\u4EBA",
  memory: "\u8BB0\u5F97\u7684\u4E8B",
  tag: "\u6807\u7B7E",
  place: "\u5730\u70B9",
  thing: "\u4E1C\u897F",
  emotion: "\u60C5\u7EEA",
  word: "\u8BCD",
  text: "\u4E00\u53E5\u8BDD"
};
var DETAIL_TEXT = {
  person: "TA \u8BB0\u5F97\u7684\u4E00\u4E2A\u4EBA\u3002",
  memory: "",
  tag: "\u4E00\u4E2A\u6807\u7B7E\uFF1A\u53E5\u5B50\u4E0A\u7684 # \u53F7\u843D\u5230\u7F51\u91CC\u5C31\u662F\u4E00\u4E2A\u70B9\u3002",
  place: "TA \u8BB0\u5F97\u7684\u4E00\u4E2A\u5730\u70B9\u3002",
  thing: "TA \u8BB0\u5F97\u7684\u4E00\u4E2A\u4E1C\u897F\u3002",
  emotion: "\u4E00\u79CD\u60C5\u7EEA\uFF1A\u8FDE\u7740\u5B83\u7684\u90A3\u679D\u662F\u611F\u53D7\uFF0C\u4E0D\u662F\u4E8B\u5B9E\u3002",
  word: "\u4E00\u4E2A\u8BCD\uFF1A\u804A\u5929\u91CC\u53CD\u590D\u51FA\u73B0\u7684\u8BF4\u6CD5\uFF0C\u81EA\u5DF1\u957F\u6210\u7684\u70B9\u3002",
  text: "\u4E00\u53E5\u539F\u6587\u3002"
};
var NODES = [
  { id: "shenyan", depth: 0, label: "\u6C88\u781A", kind: "person", net: "\u6C88\u781A", x: 210, y: 300, source: "human" },
  { id: "sweater", label: "\u5979\u8BF4\u90A3\u4EF6\u7070\u6BDB\u8863\u7A7F\u7740\u521A\u597D\uFF0C\u7F29\u6C34\u4E86\u4E5F\u6CA1\u6362", kind: "memory", net: "\u6C88\u781A", x: 210, y: 150, depth: 1, source: "auto" },
  { id: "afraid", label: "\u6015\u9ED1", kind: "emotion", net: "\u6C88\u781A", x: 350, y: 225, depth: 1, source: "auto" },
  { id: "daily", label: "\u65E5\u5E38", kind: "tag", net: "\u6C88\u781A", x: 95, y: 225, depth: 1, source: "auto" },
  { id: "stair", label: "\u6D77\u8FB9\u90A3\u6761\u8981\u722C\u5F88\u4E45\u7684\u6808\u9053\u5165\u53E3", kind: "place", net: "\u6C88\u781A", x: 330, y: 405, depth: 1, source: "auto" },
  { id: "umbrella", depth: 0, label: "\u4F1E", kind: "word", net: "\u4F1E", x: 560, y: 380, source: "auto" },
  { id: "store", label: "\u4FBF\u5229\u5E97", kind: "word", net: "\u4F1E", x: 645, y: 470, depth: 1, source: "auto" },
  { id: "outage", label: "\u505C\u7535", kind: "word", net: "\u4F1E", x: 470, y: 475, depth: 1, source: "auto" }
];
var EDGES = [
  { a: "shenyan", b: "sweater", kind: "\u5C5E\u4E8E", weight: 4 },
  { a: "shenyan", b: "afraid", kind: "\u8FDE\u7740\u60C5\u7EEA", weight: 2.5 },
  { a: "shenyan", b: "daily", kind: "\u5C5E\u4E8E", weight: 3 },
  { a: "shenyan", b: "umbrella", kind: "\u8054\u7CFB", weight: 1.5 },
  { a: "sweater", b: "daily", kind: "\u8054\u7CFB", weight: 2 },
  { a: "afraid", b: "outage", kind: "\u8FDE\u7740\u60C5\u7EEA", weight: 1.5 },
  { a: "umbrella", b: "store", kind: "\u5728\u4E00\u5904", weight: 3 },
  { a: "umbrella", b: "outage", kind: "\u5BFC\u81F4", weight: 2 },
  { a: "stair", b: "umbrella", kind: "\u8054\u7CFB", weight: 1.2 }
];
var CANVAS_W = 900;
var CANVAS_H = 600;
var byId = (id) => NODES.find((node) => node.id === id);
var degree = (id) => EDGES.filter((edge) => edge.a === id || edge.b === id).length;
var labelOf = (id) => byId(id)?.label ?? "";
var truncate = (text, max) => text.length <= max ? text : `${text.slice(0, max)}\u2026`;
var DEFAULT_PARAMS = { k: 8, fan: 1, hop: 0.55, halfLife: 60, permanent: 0.3, floor: 0.02, depth: 3 };
function MemoryGraph({ navigate, back }) {
  const [pan, setPan] = (0, import_react8.useState)({ x: 0, y: 0 });
  const [zoom, setZoom] = (0, import_react8.useState)(1);
  const [selected, setSelected] = (0, import_react8.useState)(null);
  const [net, setNet] = (0, import_react8.useState)(null);
  const [panel, setPanel] = (0, import_react8.useState)(null);
  const [params, setParams] = (0, import_react8.useState)(DEFAULT_PARAMS);
  const drag = (0, import_react8.useRef)(null);
  const moved = (0, import_react8.useRef)(false);
  const hostRef = (0, import_react8.useRef)(null);
  const nets = (0, import_react8.useMemo)(() => {
    const counts = /* @__PURE__ */ new Map();
    for (const node of NODES) counts.set(node.net, (counts.get(node.net) ?? 0) + 1);
    return [...counts.entries()].map(([name, count]) => ({ name, count }));
  }, []);
  const selectedNode = selected ? byId(selected) : void 0;
  const neighbors = (0, import_react8.useMemo)(() => {
    if (!selected) return [];
    return EDGES.filter((edge) => edge.a === selected || edge.b === selected).sort((left, right) => right.weight - left.weight).slice(0, 14).map((edge) => {
      const other = edge.a === selected ? edge.b : edge.a;
      return { id: other, label: labelOf(other), meta: `${edge.kind} \xB7 ${edge.weight.toFixed(1)}` };
    });
  }, [selected]);
  const recall = (0, import_react8.useMemo)(() => {
    if (!selected) return "";
    const fan = Math.pow(Math.max(1, degree(selected)), params.fan);
    const forward = (from, hops) => EDGES.filter((edge) => hops === 1 ? edge.a === from : edge.b === from).map((edge) => [hops === 1 ? edge.b : edge.a, edge.weight]);
    const seen = /* @__PURE__ */ new Map();
    const add = (id, score) => {
      if (id === selected) return;
      seen.set(id, Math.max(seen.get(id) ?? 0, score));
    };
    for (const [id, weight] of forward(selected, 1)) add(id, weight / (weight + params.k) / fan);
    if (params.depth > 1) {
      for (const [id, weight] of forward(selected, 1)) {
        const hopFan = Math.pow(Math.max(1, degree(id)), params.fan);
        const base = weight / (weight + params.k) / hopFan;
        for (const [next, nextWeight] of forward(id, 1)) {
          add(next, base * (nextWeight / (nextWeight + params.k)) / hopFan * params.hop);
        }
      }
    }
    const hits = [...seen.entries()].filter(([, score]) => score >= params.floor).sort((left, right) => right[1] - left[1]).slice(0, 10);
    return hits.length === 0 ? "\u8FD9\u6837\u8C03\uFF0C\u4EC0\u4E48\u90FD\u60F3\u4E0D\u8D77\u6765\u4E86\uFF08\u9608\u503C\u592A\u9AD8\u6216\u8054\u7CFB\u90FD\u65AD\u4E86\uFF09\u3002" : hits.map(([id, score]) => `${labelOf(id)} ${score.toFixed(2)}`).join("\u3000");
  }, [selected, params]);
  const summary = (0, import_react8.useMemo)(() => {
    const halfLife = params.halfLife <= 0 ? "\u4E0D\u9057\u5FD8" : `${params.halfLife.toFixed(1)} \u5929`;
    return `\u9971\u548C K=${params.k.toFixed(1)}\uFF5C\u6247\u5F62^${params.fan.toFixed(2)}\uFF5C\u4E8C\u8DF3\xD7${params.hop.toFixed(
      2
    )}\uFF5C\u534A\u8870\u671F ${halfLife}\uFF5C\u6C38\u4E45 ${Math.round(params.permanent * 100)}%\uFF5C\u9608\u503C ${params.floor.toFixed(3)}`;
  }, [params]);
  const json = (0, import_react8.useMemo)(
    () => JSON.stringify(
      {
        nodes: NODES.map((node) => ({ id: node.id, label: node.label, kind: KIND_TEXT[node.kind], source: node.source })),
        edges: EDGES
      },
      null,
      2
    ),
    []
  );
  const onPointerDown = (event) => {
    drag.current = { x: event.clientX, y: event.clientY };
    moved.current = false;
    event.currentTarget.setPointerCapture(event.pointerId);
  };
  const onPointerMove = (event) => {
    if (!drag.current) return;
    const rect = hostRef.current?.getBoundingClientRect();
    const factor = rect && rect.width > 0 ? CANVAS_W / rect.width / zoom : 1;
    const dx = (event.clientX - drag.current.x) * factor;
    const dy = (event.clientY - drag.current.y) * (rect && rect.height > 0 ? CANVAS_H / rect.height / zoom : factor);
    if (Math.abs(dx) + Math.abs(dy) > 1) moved.current = true;
    drag.current = { x: event.clientX, y: event.clientY };
    setPan((now) => ({ x: now.x + dx, y: now.y + dy }));
  };
  const onPointerUp = () => {
    drag.current = null;
  };
  const togglePanel = (which) => setPanel((now) => now === which ? null : which);
  const selectedMeta = selectedNode ? `${degree(selectedNode.id)} \u6761\u8054\u7CFB \xB7 ${selectedNode.source === "human" ? "\u4EBA\u5DE5\u6539\u8FC7" : "\u81EA\u52A8\u79EF\u7D2F"} \xB7 \u300C${selectedNode.net}\u300D\u8FD9\u4E00\u7247` + (selectedNode.depth > 0 ? ` \xB7 \u79BB\u7247\u5FC3 ${selectedNode.depth} \u8DF3` : " \xB7 \u8FD9\u7247\u7684\u5730\u6807") : "";
  return /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(
      PageHeader,
      {
        eyebrow: "// NETWORK",
        title: "\u5173\u7CFB\u7F51",
        subtitle: "\u53EA\u5728\u8FD9\u4EFD\u5B58\u6863\u91CC",
        backText: "\u2190",
        onBack: back,
        right: /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)("div", { style: { display: "flex", flexWrap: "wrap", justifyContent: "flex-end", gap: 6, maxWidth: 520 }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(GhostSm, { onClick: () => {
            setPan({ x: 0, y: 0 });
            setZoom(1);
          }, children: "\u91CD\u64AD\u751F\u957F" }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(GhostSm, { onClick: () => {
            setPan({ x: 0, y: 0 });
            setZoom(1);
          }, children: "\u94FA\u6EE1\u5C4F\u5E55" }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(GhostSm, { children: "\u8DF3\u5230\u4E00\u4E2A\u8BF4\u6CD5" }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(GhostSm, { onClick: () => navigate("/memorySearch"), children: "\u641C\u4E8B\u4EF6" }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(GhostSm, { onClick: () => togglePanel("params"), children: "\u53C2\u6570\u6A21\u578B" }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(GhostSm, { onClick: () => togglePanel("quant"), children: "\u91CF\u5316\u6A21\u578B" }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(GhostSm, { onClick: () => togglePanel("think"), children: "\u601D\u8003\u6A21\u578B" }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(GhostSm, { onClick: () => togglePanel("json"), children: "JSON" }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(TextSm, { children: "\u91CD\u65B0\u644A\u7F51" })
        ] })
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)("div", { style: { display: "flex", flexDirection: "column" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)(
        "div",
        {
          style: {
            display: "flex",
            alignItems: "center",
            gap: "var(--w-space-2)",
            padding: "var(--w-space-2) var(--w-pad-card)",
            background: "var(--w-surface-tint)",
            overflow: "hidden"
          },
          children: [
            /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)("div", { style: { display: "flex", gap: "var(--w-space-1)", overflowX: "auto", flex: 1, paddingBottom: 2 }, children: [
              /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(
                GhostSm,
                {
                  onClick: () => setNet(null),
                  style: { background: net === null ? "var(--w-surface-tint)" : void 0, fontWeight: net === null ? 700 : 400 },
                  children: "\u6574\u5F20\u90FD\u770B"
                }
              ),
              nets.map((item) => {
                const active = net === item.name;
                return /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)(
                  GhostSm,
                  {
                    onClick: () => setNet(active ? null : item.name),
                    style: {
                      background: active ? "var(--w-signal)" : void 0,
                      color: active ? "var(--w-ink-on-signal)" : void 0,
                      fontWeight: active ? 700 : void 0,
                      whiteSpace: "nowrap"
                    },
                    children: [
                      truncate(item.name, 10),
                      " ",
                      item.count
                    ]
                  },
                  item.name
                );
              })
            ] }),
            /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)(Cap, { style: { whiteSpace: "nowrap" }, children: [
              "\u957F\u597D\u4E86 \xB7 ",
              NODES.length,
              " \u4E2A\u6982\u5FF5"
            ] }),
            /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)(Cap, { style: { whiteSpace: "nowrap" }, children: [
              NODES.length,
              " \u4E2A\u6982\u5FF5 \xB7 ",
              EDGES.length,
              " \u6761\u8054\u7CFB \xB7 ",
              nets.length,
              " \u7247"
            ] })
          ]
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime12.jsx)("div", { style: { height: 1, background: "var(--w-hairline)" } })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)(
      "div",
      {
        ref: hostRef,
        style: { position: "relative", flex: 1, minHeight: 560, overflow: "hidden", background: "var(--w-bg)" },
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(
            "svg",
            {
              viewBox: `0 0 ${CANVAS_W} ${CANVAS_H}`,
              preserveAspectRatio: "xMidYMid meet",
              style: { position: "absolute", inset: 0, width: "100%", height: "100%", cursor: "grab", touchAction: "none" },
              onPointerDown,
              onPointerMove,
              onPointerUp,
              onPointerCancel: onPointerUp,
              onWheel: (event) => setZoom((value) => Math.min(2.5, Math.max(0.4, value - event.deltaY * 1e-3))),
              role: "img",
              "aria-label": "\u5173\u7CFB\u7F51",
              children: /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)("g", { transform: `translate(${pan.x} ${pan.y}) scale(${zoom})`, children: [
                EDGES.map((edge) => {
                  const from = byId(edge.a);
                  const to = byId(edge.b);
                  if (!from || !to) return null;
                  const dim = net !== null && from.net !== net && to.net !== net;
                  return /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(
                    "line",
                    {
                      x1: from.x,
                      y1: from.y,
                      x2: to.x,
                      y2: to.y,
                      style: {
                        stroke: "var(--w-ink-body)",
                        strokeWidth: 1 + Math.min(3, edge.weight * 0.5),
                        strokeOpacity: dim ? 0.06 : 0.18 + Math.min(0.5, edge.weight * 0.12)
                      }
                    },
                    `${edge.a}-${edge.b}-${edge.kind}`
                  );
                }),
                NODES.map((node) => {
                  const dim = net !== null && node.net !== net;
                  const active = selected === node.id;
                  const radius = node.kind === "person" ? 13 : node.kind === "memory" ? 9 : 7;
                  return /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)(
                    "g",
                    {
                      transform: `translate(${node.x} ${node.y})`,
                      opacity: dim ? 0.28 : 1,
                      style: { cursor: "pointer" },
                      onClick: (event) => {
                        event.stopPropagation();
                        if (!moved.current) setSelected(node.id);
                      },
                      children: [
                        /* @__PURE__ */ (0, import_jsx_runtime12.jsx)("title", { children: node.label }),
                        active ? /* @__PURE__ */ (0, import_jsx_runtime12.jsx)("circle", { r: radius + 5, style: { fill: "none", stroke: "var(--w-signal)", strokeWidth: 2 } }) : null,
                        /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(
                          "circle",
                          {
                            r: radius,
                            style: {
                              fill: node.kind === "person" ? "var(--w-signal)" : "var(--w-surface-tint)",
                              stroke: node.kind === "person" ? "var(--w-signal)" : "var(--w-ink-muted)",
                              strokeWidth: 1.2
                            }
                          }
                        ),
                        node.source === "auto" ? /* @__PURE__ */ (0, import_jsx_runtime12.jsx)("circle", { cx: radius - 1, cy: -radius + 1, r: 2.5, style: { fill: "var(--w-ink-muted)" } }) : null,
                        /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(
                          "text",
                          {
                            y: radius + 13,
                            textAnchor: "middle",
                            style: { fontSize: 11, fill: "var(--w-ink-body)", fontFamily: "var(--w-font-ui)" },
                            children: truncate(node.label, 12)
                          }
                        )
                      ]
                    },
                    node.id
                  );
                })
              ] })
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)(
            "div",
            {
              style: {
                position: "absolute",
                left: 10,
                bottom: 10,
                width: 246,
                padding: "10px 12px",
                borderRadius: "var(--w-radius-card)",
                border: "1px solid var(--w-hairline)",
                background: "var(--w-surface-tint)",
                display: "flex",
                flexDirection: "column",
                gap: "var(--w-space-1)"
              },
              children: [
                net ? /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)(Cap, { children: [
                  "\u53EA\u770B\u8FD9\u4E00\u7247\uFF1A",
                  net
                ] }) : null,
                /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)("div", { style: { display: "flex", gap: "var(--w-space-1)", margin: "2px 0" }, children: [
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(GhostSm, { width: 34, style: { padding: 0 }, onClick: () => setZoom((v) => Math.max(0.4, v - 0.15)), children: "\uFF0D" }),
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(GhostSm, { width: 34, style: { padding: 0 }, onClick: () => setZoom((v) => Math.min(2.5, v + 0.15)), children: "\uFF0B" }),
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(GhostSm, { onClick: () => {
                    setPan({ x: 0, y: 0 });
                    setZoom(1);
                  }, children: "\u94FA\u6EE1" })
                ] }),
                /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Cap, { children: "\u62D6\u52A8=\u632A\u753B\u9762 \xB7 \u6EDA\u8F6E=\u7F29\u653E \xB7 \u70B9\u4E00\u4E0B=\u9009\u4E2D \xB7 \u518D\u70B9\u4E00\u4E0B=\u9489\u4F4F\u5B83" })
              ]
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)(
            "div",
            {
              style: {
                position: "absolute",
                right: 12,
                top: 10,
                bottom: 10,
                width: 352,
                maxWidth: "calc(100% - 24px)",
                overflowY: "auto",
                display: "flex",
                flexDirection: "column",
                gap: "var(--w-space-2)"
              },
              children: [
                selectedNode ? /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)(
                  "div",
                  {
                    style: {
                      padding: "10px 12px",
                      borderRadius: "var(--w-radius-card)",
                      border: "1px solid var(--w-hairline)",
                      background: "var(--w-surface-tint)",
                      display: "flex",
                      flexDirection: "column",
                      gap: "var(--w-space-1)"
                    },
                    children: [
                      /* @__PURE__ */ (0, import_jsx_runtime12.jsx)("span", { style: { fontSize: "var(--w-size-small)", fontWeight: 700, color: "var(--w-ink-display)" }, children: selectedNode.label }),
                      /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Cap, { children: KIND_TEXT[selectedNode.kind] }),
                      /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Cap, { children: selectedMeta }),
                      /* @__PURE__ */ (0, import_jsx_runtime12.jsx)("span", { style: { fontSize: "var(--w-size-body)", color: "var(--w-ink-body)", lineHeight: 1.6 }, children: selectedNode.kind === "memory" ? selectedNode.label : DETAIL_TEXT[selectedNode.kind] }),
                      /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)("div", { style: { display: "flex", gap: "var(--w-space-1)", marginTop: 2, flexWrap: "wrap" }, children: [
                        /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(GhostSm, { children: "\u9489\u4F4F\u5B83" }),
                        /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(GhostSm, { children: "\u548C\u5B83\u8FDE\u2026" }),
                        /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(GhostSm, { style: { color: "var(--w-signal)" }, children: "\u5FD8\u6389" })
                      ] }),
                      /* @__PURE__ */ (0, import_jsx_runtime12.jsx)("div", { style: { height: 1, background: "var(--w-hairline)", margin: "4px 0" } }),
                      /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Cap, { children: "\u8EAB\u4E0A\u7684\u8054\u7CFB\uFF08\u70B9\u4E00\u6761\u8FC7\u53BB\u770B\uFF09" }),
                      /* @__PURE__ */ (0, import_jsx_runtime12.jsx)("div", { style: { display: "flex", flexDirection: "column", gap: 2 }, children: neighbors.map((row) => /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)(
                        "button",
                        {
                          type: "button",
                          onClick: () => setSelected(row.id),
                          style: {
                            display: "flex",
                            gap: "var(--w-space-1)",
                            padding: "5px 6px",
                            border: 0,
                            borderRadius: "var(--w-radius-well)",
                            background: "transparent",
                            font: "inherit",
                            textAlign: "left",
                            cursor: "pointer"
                          },
                          children: [
                            /* @__PURE__ */ (0, import_jsx_runtime12.jsx)("span", { style: { fontSize: 12, color: "var(--w-ink-display)", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }, children: truncate(row.label, 16) }),
                            /* @__PURE__ */ (0, import_jsx_runtime12.jsx)("span", { style: { fontSize: 11, color: "var(--w-ink-muted)", whiteSpace: "nowrap" }, children: row.meta })
                          ]
                        },
                        row.id
                      )) }),
                      /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Cap, { children: "\u70B9\u679D\uFF08\u4E0D\u662F\u70B9\u7599\u7629\uFF09\u80FD\u9009\u4E2D\u4E00\u6761\u8054\u7CFB\uFF0C\u7136\u540E\u8C03\u5F31\u6216\u65AD\u5F00\u3002" }),
                      /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)("div", { style: { display: "flex", gap: "var(--w-space-1)" }, children: [
                        /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(GhostSm, { children: "\u8C03\u5F31\u8FD9\u6761" }),
                        /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(GhostSm, { style: { color: "var(--w-signal)" }, children: "\u65AD\u5F00\u8FD9\u6761" })
                      ] })
                    ]
                  }
                ) : null,
                panel === "params" ? /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)(Panel, { title: "\u53C2\u6570\u6A21\u578B\uFF1A\u8FD9\u5F20\u7F51\u600E\u4E48\u7B97", children: [
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Cap, { children: "\u540C\u6837\u7684\u7F51\uFF0C\u53C2\u6570\u4E0D\u540C\u5C31\u662F\u4E0D\u540C\u7684\u8111\u5B50\u3002\u6539\u4E00\u4E2A\u6570\u5B57\uFF0C\u4E0B\u9762\u7ACB\u523B\u6309\u65B0\u53C2\u6570\u91CD\u7B97\u300C\u73B0\u5728\u80FD\u60F3\u8D77\u4EC0\u4E48\u300D\u3002" }),
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(ParamSlider, { label: "\u8054\u60F3\u9971\u548C K\uFF1A\u8981\u591A\u5F3A\u7684\u8054\u7CFB\u624D\u7B97\u6570", min: 0.5, max: 40, step: 0.5, value: params.k, onChange: (k) => setParams((now) => ({ ...now, k })) }),
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(ParamSlider, { label: "\u6247\u5F62\u6291\u5236\uFF1A\u8FDE\u5F97\u8D8A\u6742\uFF0C\u5355\u6761\u8D8A\u5BB9\u6613\u88AB\u6324\u6389\uFF080=\u4E0D\u6291\u5236\uFF09", min: 0, max: 2, step: 0.05, value: params.fan, onChange: (fan) => setParams((now) => ({ ...now, fan })) }),
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(ParamSlider, { label: "\u4E8C\u8DF3\u8870\u51CF\uFF1A\u60F3\u5230\u7B2C\u4E8C\u6B65\u65F6\u8FD8\u5269\u591A\u5C11", min: 0.05, max: 1, step: 0.05, value: params.hop, onChange: (hop) => setParams((now) => ({ ...now, hop })) }),
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(ParamSlider, { label: "\u8054\u7CFB\u534A\u8870\u671F\uFF08\u5929\uFF0C0=\u4E0D\u5FD8\uFF09", min: 0, max: 365, step: 1, value: params.halfLife, onChange: (halfLife) => setParams((now) => ({ ...now, halfLife })) }),
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(ParamSlider, { label: "\u6C38\u4E45\u6210\u5206\uFF1A\u4E45\u5230\u6781\u9650\u8FD8\u5269\u51E0\u5206", min: 0, max: 0.9, step: 0.05, value: params.permanent, onChange: (permanent) => setParams((now) => ({ ...now, permanent })) }),
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(ParamSlider, { label: "\u60F3\u4E0D\u8D77\u6765\u7684\u7EBF\uFF08\u4F4E\u4E8E\u5B83\u5C31\u5F53\u6CA1\u60F3\u5230\uFF09", min: 5e-3, max: 0.2, step: 5e-3, value: params.floor, onChange: (floor) => setParams((now) => ({ ...now, floor })) }),
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(ParamSlider, { label: "\u5F80\u5916\u770B\u51E0\u5708", min: 1, max: 4, step: 1, value: params.depth, onChange: (depth) => setParams((now) => ({ ...now, depth })) }),
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Cap, { children: summary }),
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)("div", { style: { height: 1, background: "var(--w-hairline)" } }),
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Cap, { children: "\u73B0\u5728\u987A\u7740\u9489\u4F4F\u7684\u90A3\u4E2A\u8BF4\u6CD5\u80FD\u60F3\u8D77\uFF1A" }),
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)("span", { style: { fontSize: "var(--w-size-body)", color: "var(--w-ink-body)", lineHeight: 1.6 }, children: recall || "\u5148\u5728\u7F51\u91CC\u70B9\u4E00\u4E2A\u70B9\uFF0C\u8FD9\u91CC\u5C31\u6309\u5F53\u524D\u53C2\u6570\u7B97\u7ED9\u5B83\u770B\u3002" }),
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)("div", { style: { display: "flex", gap: "var(--w-space-1)" }, children: [
                    /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(PrimarySm, { children: "\u5B58\u6210 TA \u7684\u8054\u60F3\u65B9\u5F0F" }),
                    /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(GhostSm, { onClick: () => setParams(DEFAULT_PARAMS), children: "\u56DE\u5230\u9ED8\u8BA4" })
                  ] }),
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Cap, { children: "\u5B58\u4E86\u4E4B\u540E\uFF0C\u804A\u5929\u65F6\u68C0\u7D22\u8BB0\u5FC6\u4E5F\u6309\u8FD9\u5957\u53C2\u6570\u8D70 \u2014\u2014 \u4E0D\u53EA\u662F\u753B\u9762\u53D8\u4E86\u3002" })
                ] }) : null,
                panel === "quant" ? /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)(Panel, { title: "\u91CF\u5316\u6A21\u578B\uFF1A\u628A\u8BF4\u6CD5\u6362\u7B97\u6210\u6570\u503C", children: [
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Cap, { children: "\u77E5\u8BC6\u5E93\u7BA1\u7406\u5458\u5E72\u7684\u662F\u8FD9\u4EF6\u4E8B\uFF1A\u8BCD\u8FDB\u53BB\u3001\u4E00\u7EC4\u6570\u503C\u51FA\u6765\uFF0C\u6D3B\u5F97\u50CF\u540C\u4E00\u7C7B\u4E1C\u897F\u7684\u8BF4\u6CD5\u6570\u503C\u5C31\u6328\u5F97\u8FD1\u3002\u5B83\u770B\u5F97\u89C1\u4ECE\u6CA1\u540C\u6846\u51FA\u73B0\u8FC7\u7684\u8FD1\u4E49 \u2014\u2014 \u5171\u73B0\u7EDF\u8BA1\u548C\u5168\u6587\u68C0\u7D22\u90FD\u5230\u4E0D\u4E86\u8FD9\u4E00\u5C42\u3002" }),
                  selectedNode ? /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)(Cap, { children: [
                    "\u6B63\u5728\u95EE\uFF1A\u300C",
                    truncate(selectedNode.label, 20),
                    "\u300D"
                  ] }) : null,
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Cap, { children: "\u672C\u5730\u7EDF\u8BA1\u51D1\u7684" }),
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Cap, { children: selectedNode ? "\u6570\u503C\u4E0A\u6CA1\u627E\u5230\u50CF\u7684\uFF1A\u8FD9\u4E2A\u8BF4\u6CD5\u8FD8\u6CA1\u6512\u591F\u8BED\u5883\u3002\u591A\u804A\u51E0\u8F6E\u518D\u6765\u770B\u3002" : "\u5148\u5728\u7F51\u91CC\u70B9\u4E00\u4E2A\u70B9\uFF08\u6216\u4E00\u6BB5\u8BB0\u5F97\u7684\u4E8B\uFF09\uFF0C\u8FD9\u91CC\u5C31\u5217\u51FA\u6570\u503C\u4E0A\u6700\u63A5\u8FD1\u5B83\u7684\u4E00\u6279\u8BF4\u6CD5\u3002" }),
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(PrimarySm, { children: "\u6574\u5F20\u7F51\u6309\u6570\u503C\u518D\u7EC7\u4E00\u904D" }),
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Cap, { children: "\u300C\u7275\u4E0A\u300D= \u628A\u8FD9\u4E00\u5BF9\u6309\u6570\u503C\u843D\u4E00\u6761\u7EBF\uFF1B\u300C\u518D\u7EC7\u4E00\u904D\u300D= \u8BA9\u6A21\u578B\u628A\u6240\u6709\u6328\u5F97\u8FD1\u53C8\u6CA1\u8FDE\u4E0A\u7684\u5BF9\u5B50\u90FD\u8865\u4E00\u904D\uFF08\u8D35\uFF0C\u6309\u4E00\u6B21\u5C31\u597D\uFF09\u3002" })
                ] }) : null,
                panel === "think" ? /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)(Panel, { title: "\u601D\u8003\u6A21\u578B\uFF1A\u8BA9\u6A21\u578B\u8BFB\u4E00\u904D\u7F51", children: [
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Cap, { children: "\u4E0B\u9762\u5217\u51FA\u7684\u662F\u6240\u6709\u914D\u597D\u7684\u5BF9\u8BDD\u6A21\u578B\uFF0C\u4EFB\u4F55\u4E00\u6B3E\u90FD\u80FD\u5F53\u601D\u8003\u6A21\u578B\u6765\u7528\u3002\u540D\u5B57\u5E26\u300C\u4F1A\u60F3\u300D\u7684\u4F1A\u5148\u60F3\u4E00\u6B65\u518D\u7B54\uFF0C\u5176\u4F59\u7684\u76F4\u63A5\u51FA\u5EFA\u8BAE \u2014\u2014 \u8FD9\u4E0D\u662F\u4E00\u9053\u95E8\u69DB\u3002\u5EFA\u8BAE\u90FD\u8981\u4F60\u70B9\u5934\u624D\u843D\u5730\u3002" }),
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(PickerPlaceholder, { title: "\u9009\u4E00\u4E2A\u6A21\u578B" }),
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)("div", { style: { display: "flex", gap: "var(--w-space-1)", flexWrap: "wrap" }, children: /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(PrimarySm, { children: "\u8BFB\u7F51\u51FA\u5EFA\u8BAE" }) }),
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Cap, { children: "\u5148\u5728\u8BBE\u7F6E\u91CC\u586B\u4E00\u4E2A\u5BF9\u8BDD\u6A21\u578B\uFF0C\u8FD9\u91CC\u624D\u6709\u5F97\u9009\u3002" })
                ] }) : null,
                panel === "json" ? /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)(Panel, { title: "\u8FD9\u5F20\u7F51\u7684 JSON\uFF08\u548C\u78C1\u76D8\u4E0A\u7684\u6587\u4EF6\u540C\u4E00\u4EFD\uFF09", children: [
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Cap, { children: "\u8282\u70B9\u3001\u8FB9\u3001\u6743\u91CD\u3001\u6765\u5386\u90FD\u5728\u91CC\u9762\uFF0C\u6539\u5B83\u7B49\u4E8E\u6539 TA \u7684\u8111\u5B50\u3002" }),
                  /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(
                    "pre",
                    {
                      style: {
                        margin: 0,
                        padding: "8px 10px",
                        borderRadius: "var(--w-radius-well)",
                        border: "1px solid var(--w-hairline)",
                        background: "var(--w-bg)",
                        color: "var(--w-ink-body)",
                        fontSize: 11,
                        lineHeight: 1.5,
                        maxHeight: 360,
                        overflow: "auto",
                        whiteSpace: "pre-wrap"
                      },
                      children: json
                    }
                  )
                ] }) : null
              ]
            }
          )
        ]
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)(
      "div",
      {
        style: {
          display: "flex",
          alignItems: "center",
          gap: "var(--w-space-3)",
          padding: "7px 12px",
          overflowX: "auto",
          borderTop: "1px solid var(--w-hairline)",
          background: "var(--w-surface-tint)"
        },
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Legend, { kind: "person", label: "\u4EBA" }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Legend, { kind: "memory", label: "\u8BB0\u5F97\u7684\u4E8B" }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Legend, { kind: "tag", label: "\u6807\u7B7E" }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Legend, { kind: "place", label: "\u5730\u70B9" }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Legend, { kind: "thing", label: "\u4E1C\u897F" }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Legend, { kind: "emotion", label: "\u60C5\u7EEA" }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Legend, { kind: "word", label: "\u8BCD" }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)("span", { style: { width: 1, height: 16, background: "var(--w-hairline)" } }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)("span", { style: { display: "flex", flexDirection: "column", gap: 2 }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime12.jsx)("span", { style: { display: "block", height: 2, width: 24, background: "var(--w-ink-display)", opacity: 0.72 } }),
            /* @__PURE__ */ (0, import_jsx_runtime12.jsx)("span", { style: { display: "block", height: 2, width: 24, background: "var(--w-ink-display)", opacity: 0.72 } })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Cap, { children: "\u7B49\u4EF7\uFF08\u5C31\u662F\u5B83\uFF09" }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)("span", { style: { height: 4, width: 24, background: "var(--w-ink-display)", opacity: 0.55, borderRadius: 2 } }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Cap, { children: "\u7247\u91CC\u7684\u679D" }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)("span", { style: { height: 1, width: 24, background: "var(--w-ink-display)", opacity: 0.4 } }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Cap, { children: "\u8DE8\u7247\u7684\u7EC6\u85E4" }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)("span", { style: { height: 3, width: 24, background: "var(--w-ink-display)", opacity: 0.16, borderRadius: 2 } }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Cap, { children: "\u6DE1=\u5FEB\u8981\u60F3\u4E0D\u8D77\u6765\u4E86" }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)("span", { style: { width: 8, height: 8, borderRadius: 4, background: "var(--w-ink-muted)" } }),
          /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Cap, { children: "\u8FD9\u4E2A\u70B9\u662F AI \u6574\u7406\u51FA\u6765\u7684" })
        ]
      }
    )
  ] });
}
function Panel({ title: title2, children }) {
  return /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)(
    "div",
    {
      style: {
        padding: "10px 12px",
        borderRadius: "var(--w-radius-card)",
        border: "1px solid var(--w-hairline)",
        background: "var(--w-surface-tint)",
        display: "flex",
        flexDirection: "column",
        gap: "var(--w-space-2)"
      },
      children: [
        /* @__PURE__ */ (0, import_jsx_runtime12.jsx)("span", { style: { fontSize: "var(--w-size-small)", fontWeight: 700, color: "var(--w-ink-display)" }, children: title2 }),
        children
      ]
    }
  );
}
function ParamSlider({
  label,
  min,
  max,
  step,
  value,
  onChange
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)("label", { style: { display: "flex", flexDirection: "column", gap: 4 }, children: [
    /* @__PURE__ */ (0, import_jsx_runtime12.jsx)("span", { style: { fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)" }, children: label }),
    /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(
      "input",
      {
        type: "range",
        min,
        max,
        step,
        value,
        onChange: (event) => onChange(Number(event.target.value)),
        style: { width: "100%", accentColor: "var(--w-signal)" }
      }
    )
  ] });
}
function Legend({ kind, label }) {
  const square = kind === "memory";
  return /* @__PURE__ */ (0, import_jsx_runtime12.jsxs)("span", { style: { display: "inline-flex", alignItems: "center", gap: "var(--w-space-1)", whiteSpace: "nowrap" }, children: [
    /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(
      "span",
      {
        style: {
          display: "block",
          width: square ? 13 : 11,
          height: square ? 9 : 11,
          borderRadius: square ? 2 : 6,
          background: kind === "person" ? "var(--w-signal)" : "var(--w-surface-tint)",
          border: square || kind !== "person" ? "1px solid var(--w-hairline)" : 0
        }
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime12.jsx)(Cap, { children: label })
  ] });
}

// src/screens/character-media/memorySearch.tsx
var import_react9 = __toESM(require_react(), 1);
var import_jsx_runtime13 = __toESM(require_jsx_runtime(), 1);
var KINDS = ["\u5168\u90E8", "\u4E8B\u4EF6", "\u91CD\u8981\u65F6\u523B", "\u5370\u8C61", "\u7167\u7247"];
function MemorySearch({ navigate, back }) {
  const [query, setQuery] = (0, import_react9.useState)("");
  const [kind, setKind] = (0, import_react9.useState)("\u5168\u90E8");
  const [chain, setChain] = (0, import_react9.useState)(false);
  return /* @__PURE__ */ (0, import_jsx_runtime13.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime13.jsx)(PageHeader, { title: "\u641C\u4E8B\u4EF6", onBack: back }),
    /* @__PURE__ */ (0, import_jsx_runtime13.jsxs)("div", { className: "screen", children: [
      /* @__PURE__ */ (0, import_jsx_runtime13.jsxs)(Card, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime13.jsxs)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-2)", flexWrap: "wrap" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime13.jsx)(
            "input",
            {
              className: "k-field",
              style: { flex: 1, minWidth: 180, fontSize: "var(--w-size-body)" },
              value: query,
              placeholder: "\u4E00\u53E5\u8BDD\u3001#\u6807\u7B7E\u3001\u4E00\u79CD\u611F\u89C9\u3001\u6216\u8005 8\u670824\u53F7\u2026",
              "aria-label": "\u641C\u4E8B\u4EF6",
              onChange: (event) => setQuery(event.target.value)
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime13.jsx)(TextSm, { onClick: () => setQuery(""), children: "\u6E05\u7A7A" }),
          /* @__PURE__ */ (0, import_jsx_runtime13.jsx)(
            GhostSm,
            {
              onClick: () => setChain((v) => !v),
              style: {
                background: chain ? "var(--w-surface-tint)" : void 0,
                fontWeight: chain ? 700 : 400,
                color: chain ? "var(--w-ink-display)" : void 0
              },
              children: chain ? "\u94FE\u5F0F \xB7 \u5F00" : "\u94FE\u5F0F"
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime13.jsx)(GhostSm, { onClick: () => navigate("/memoryGraph"), children: "\u5173\u7CFB\u7F51" })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime13.jsx)("div", { style: { display: "flex", flexWrap: "wrap", gap: "var(--w-space-1)" }, children: KINDS.map((item) => {
          const active = item === kind;
          return /* @__PURE__ */ (0, import_jsx_runtime13.jsx)(
            GhostSm,
            {
              onClick: () => setKind(item),
              style: {
                background: active ? "var(--w-surface-tint)" : void 0,
                fontWeight: active ? 700 : 400,
                color: active ? "var(--w-ink-display)" : void 0
              },
              children: item
            },
            item
          );
        }) }),
        /* @__PURE__ */ (0, import_jsx_runtime13.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime13.jsx)(Cap, { children: query.trim() ? `\u300C${query.trim()}\u300D\u547D\u4E2D 0 \u4EF6` + (kind === "\u5168\u90E8" ? "" : ` \xB7 \u53EA\u770B${kind}`) : "TA \u8BB0\u5F97 0 \u4EF6\uFF08\u6700\u65B0\u5728\u8FD9\u4E9B\uFF09" }),
          /* @__PURE__ */ (0, import_jsx_runtime13.jsx)(Cap, { children: "\u53EA\u5728\u8FD9\u4EFD\u5B58\u6863\u91CC" })
        ] })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime13.jsxs)(
        "div",
        {
          style: {
            padding: "var(--w-space-3)",
            borderRadius: "var(--w-radius-card)",
            border: "1px solid var(--w-hairline)",
            background: "var(--w-surface-tint)",
            display: "flex",
            flexDirection: "column",
            gap: "var(--w-space-1)"
          },
          children: [
            /* @__PURE__ */ (0, import_jsx_runtime13.jsx)("span", { style: { fontSize: "var(--w-size-body)", fontWeight: 700, color: "var(--w-ink-body)" }, children: "\u6CA1\u627E\u7740\u3002" }),
            /* @__PURE__ */ (0, import_jsx_runtime13.jsx)(Cap, { children: query.trim() ? chain ? `\u987A\u7740\u300C${query.trim()}\u300D\u4E00\u6761\u4E5F\u6CA1\u8FDE\u4E0A\u3002\u6362\u4E2A\u7EBF\u7D22\uFF1A\u90A3\u4E2A\u4EBA\u540D\u3001\u90A3\u5929\u7684\u5929\u6C14\u3001\u5F53\u65F6\u8BF4\u8FC7\u7684\u534A\u53E5\u8BDD\u3002` : `\u6CA1\u6709\u54EA\u4EF6\u4E8B\u5BF9\u4E0A\u300C${query.trim()}\u300D\u3002\u6362\u6761\u8DEF\u8BD5\u8BD5\uFF1A\u8D34\u8FC7\u7684\u6807\u7B7E\u540D\u3001\u90A3\u79CD\u611F\u89C9\u7684\u540D\u5B57\u3001\u6216\u8005\u90A3\u5929\u7684\u65E5\u5B50\uFF088\u670824\u53F7\uFF09\u3002` : "\u8FD8\u6CA1\u8BB0\u4E0B\u4EC0\u4E48\u4E8B\u3002\u591A\u804A\u51E0\u53E5\uFF0C\u91CD\u8981\u7684\u90A3\u4E9B\u4F1A\u88AB TA \u81EA\u5DF1\u8BB0\u4F4F\u3002" })
          ]
        }
      )
    ] })
  ] });
}

// src/screens/character-media/index.tsx
var screens = {
  gallery: Gallery,
  outfit: Outfit,
  figurePack: FigurePack,
  materials: Materials,
  portraitCalibration: PortraitCalibration,
  avatarcrop: Avatarcrop,
  memoryManage: MemoryManage,
  memoryGraph: MemoryGraph,
  memorySearch: MemorySearch
};

// src/screens/character/newcharacter.tsx
var import_react12 = __toESM(require_react(), 1);

// src/lib/usePress.ts
var import_react10 = __toESM(require_react(), 1);
var clamp01 = (v) => Math.min(1, Math.max(0, v));
function bindPress(el, name = "--press") {
  let value = 0;
  let handle = null;
  const write = (v) => {
    value = v;
    el.style.setProperty(name, v.toFixed(3));
  };
  return {
    set(next) {
      const target = clamp01(next);
      handle?.cancel();
      handle = tween({ press: value }, { press: target }, canonical(PRESS_MS), (v) => {
        write(v.press);
      });
    },
    dispose() {
      handle?.cancel();
    }
  };
}
function usePress(external) {
  const local = (0, import_react10.useRef)(null);
  const ref = external ?? local;
  const control = (0, import_react10.useRef)(null);
  (0, import_react10.useEffect)(() => {
    const el = ref.current;
    if (!el) return;
    control.current = bindPress(el);
    return () => {
      control.current?.dispose();
      control.current = null;
    };
  }, [ref]);
  const down = (0, import_react10.useCallback)(() => control.current?.set(1), []);
  const up = (0, import_react10.useCallback)(() => control.current?.set(0), []);
  const flash = (0, import_react10.useCallback)(() => {
    control.current?.set(1);
    window.setTimeout(() => control.current?.set(0), 120);
  }, []);
  return { ref, down, up, flash };
}

// src/components/GlassButton.tsx
var import_jsx_runtime14 = __toESM(require_jsx_runtime(), 1);
function GlassButton({ children, onClick, size = "md", className = "", ariaLabel }) {
  const press = usePress();
  return /* @__PURE__ */ (0, import_jsx_runtime14.jsx)(
    Glass,
    {
      as: "button",
      type: "button",
      tone: "button",
      radius: 8,
      className: `gbtn gbtn--${size} ${className}`.trim(),
      elementRef: press.ref,
      "aria-label": ariaLabel,
      onPointerDown: press.down,
      onPointerUp: press.up,
      onPointerLeave: press.up,
      onPointerCancel: press.up,
      onClick,
      onKeyDown: (event) => {
        if (event.key === "Enter" || event.key === " ") press.flash();
      },
      children
    }
  );
}

// src/lib/useIsWide.ts
var import_react11 = __toESM(require_react(), 1);
function useIsWide(breakpoint = 900) {
  const query = `(min-width: ${breakpoint + 1}px)`;
  const [wide, setWide] = (0, import_react11.useState)(() => window.matchMedia(query).matches);
  (0, import_react11.useEffect)(() => {
    const media = window.matchMedia(query);
    const sync = () => setWide(media.matches);
    sync();
    media.addEventListener("change", sync);
    return () => media.removeEventListener("change", sync);
  }, [query]);
  return wide;
}

// src/components/GlassSwitch.tsx
var import_jsx_runtime15 = __toESM(require_jsx_runtime(), 1);
function GlassSwitch({ checked, onChange, label, hideLabel, disabled }) {
  const press = usePress();
  const toggle = () => onChange(!checked);
  return /* @__PURE__ */ (0, import_jsx_runtime15.jsx)(
    "button",
    {
      ref: press.ref,
      type: "button",
      role: "switch",
      "aria-checked": checked,
      "aria-label": hideLabel ? void 0 : label,
      "aria-disabled": disabled || void 0,
      disabled,
      className: "switch",
      onPointerDown: press.down,
      onPointerUp: press.up,
      onPointerLeave: press.up,
      onPointerCancel: press.up,
      onClick: toggle,
      onKeyDown: (event) => {
        if (event.key === " " || event.key === "Enter") {
          event.preventDefault();
          press.flash();
          toggle();
        }
      },
      children: /* @__PURE__ */ (0, import_jsx_runtime15.jsxs)(
        Glass,
        {
          tone: "track",
          radius: 13.5,
          className: "switch__track",
          style: { "--on": checked ? 1 : 0 },
          children: [
            /* @__PURE__ */ (0, import_jsx_runtime15.jsx)("span", { className: "switch__liquid", "aria-hidden": "true" }),
            /* @__PURE__ */ (0, import_jsx_runtime15.jsx)(Glass, { tone: "knob", radius: 10.5, className: "switch__knob" })
          ]
        }
      )
    }
  );
}

// src/screens/character/parts.tsx
var import_jsx_runtime16 = __toESM(require_jsx_runtime(), 1);
function Stack({
  gap = 3,
  children,
  style
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime16.jsx)("div", { style: { display: "flex", flexDirection: "column", gap: `var(--w-space-${gap})`, ...style }, children });
}
function Eyebrow({ children }) {
  return /* @__PURE__ */ (0, import_jsx_runtime16.jsx)("span", { className: "eyebrow", children });
}
function PaneTitle({ children }) {
  return /* @__PURE__ */ (0, import_jsx_runtime16.jsx)(
    "h3",
    {
      style: {
        margin: 0,
        fontSize: "var(--w-size-subhead)",
        fontWeight: 600,
        color: "var(--w-ink-display)"
      },
      children
    }
  );
}
function Caption({ children, faint }) {
  return /* @__PURE__ */ (0, import_jsx_runtime16.jsx)(
    "span",
    {
      style: {
        fontSize: "var(--w-size-caption)",
        color: faint ? "var(--w-ink-faint)" : "var(--w-ink-muted)",
        lineHeight: 1.55
      },
      children
    }
  );
}
function BodySm({ children }) {
  return /* @__PURE__ */ (0, import_jsx_runtime16.jsx)("span", { style: { fontSize: "var(--w-size-small)", color: "var(--w-ink-body)", lineHeight: 1.5 }, children });
}
function SectionHead({ no, eyebrow: eyebrow2, title: title2 }) {
  return /* @__PURE__ */ (0, import_jsx_runtime16.jsxs)(Stack, { gap: 2, children: [
    /* @__PURE__ */ (0, import_jsx_runtime16.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "40px 1fr", columnGap: "var(--w-space-2)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime16.jsx)(
        "span",
        {
          style: {
            fontFamily: "var(--w-font-num)",
            fontSize: "26px",
            color: "var(--w-ink-faint)",
            alignSelf: "center"
          },
          children: no
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime16.jsxs)("div", { style: { display: "flex", flexDirection: "column" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime16.jsx)(Eyebrow, { children: eyebrow2 }),
        /* @__PURE__ */ (0, import_jsx_runtime16.jsx)(PaneTitle, { children: title2 })
      ] })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime16.jsx)(Hair, {})
  ] });
}
function Hair({ inset }) {
  return /* @__PURE__ */ (0, import_jsx_runtime16.jsx)(
    "div",
    {
      style: {
        height: 1,
        background: "var(--w-hairline)",
        marginLeft: inset ? "var(--w-pad-card)" : 0,
        marginRight: inset ? "var(--w-pad-card)" : 0
      }
    }
  );
}
function Note({ children, warn }) {
  return /* @__PURE__ */ (0, import_jsx_runtime16.jsx)(
    "p",
    {
      style: {
        margin: 0,
        fontSize: "var(--w-size-caption)",
        color: warn ? "var(--w-signal)" : "var(--w-ink-muted)",
        lineHeight: 1.6
      },
      children
    }
  );
}
function ChipBtn({
  label,
  on,
  onClick
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime16.jsx)(
    "button",
    {
      type: "button",
      "aria-pressed": on || void 0,
      onClick,
      style: {
        padding: "5px 10px",
        minHeight: 30,
        borderRadius: 99,
        border: on ? "1px solid transparent" : "1px solid var(--w-hairline)",
        background: on ? "var(--w-ink-display)" : "var(--w-tint-button)",
        color: on ? "var(--w-bg)" : "var(--w-ink-body)",
        font: "inherit",
        fontSize: "var(--w-size-caption)",
        cursor: "pointer"
      },
      children: label
    }
  );
}
function Chips({ children }) {
  return /* @__PURE__ */ (0, import_jsx_runtime16.jsx)("div", { style: { display: "flex", flexWrap: "wrap", gap: 6 }, children });
}
function SwitchLine({
  title: title2,
  desc: desc2,
  checked,
  onChange
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime16.jsxs)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-3)" }, children: [
    /* @__PURE__ */ (0, import_jsx_runtime16.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 2, flex: 1, minWidth: 0 }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime16.jsx)(BodySm, { children: title2 }),
      desc2 ? /* @__PURE__ */ (0, import_jsx_runtime16.jsx)(Caption, { children: desc2 }) : null
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime16.jsx)(GlassSwitch, { checked, onChange, label: title2 })
  ] });
}
function SliderLine({
  label,
  reading,
  value,
  onChange,
  disabled
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime16.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 2 }, children: [
    /* @__PURE__ */ (0, import_jsx_runtime16.jsxs)("div", { style: { display: "flex", alignItems: "baseline", justifyContent: "space-between", gap: "var(--w-space-2)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime16.jsx)("span", { style: { fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)" }, children: label }),
      reading ? /* @__PURE__ */ (0, import_jsx_runtime16.jsx)(Caption, { children: reading }) : null
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime16.jsx)(
      "input",
      {
        type: "range",
        min: 0,
        max: 100,
        step: 1,
        value: Math.round(value),
        disabled,
        "aria-label": label,
        onChange: (event) => onChange(Number(event.target.value)),
        style: {
          width: "100%",
          accentColor: "var(--w-signal)",
          background: "transparent",
          cursor: disabled ? "default" : "pointer",
          opacity: disabled ? 0.45 : 1
        }
      }
    )
  ] });
}
function Actions({ children, end }) {
  return /* @__PURE__ */ (0, import_jsx_runtime16.jsx)(
    "div",
    {
      style: {
        display: "flex",
        flexWrap: "wrap",
        alignItems: "center",
        justifyContent: end ? "flex-end" : "flex-start",
        gap: 6
      },
      children
    }
  );
}
function Well2({ children }) {
  return /* @__PURE__ */ (0, import_jsx_runtime16.jsx)(
    "div",
    {
      style: {
        padding: "var(--w-space-3)",
        borderRadius: 4,
        background: "var(--w-surface-tint)",
        display: "flex",
        flexDirection: "column",
        gap: "var(--w-space-3)"
      },
      children
    }
  );
}
function PrimaryBtn({
  children,
  onClick,
  disabled,
  block
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime16.jsx)(
    "button",
    {
      type: "button",
      onClick,
      disabled,
      style: {
        height: 32,
        padding: "0 14px",
        width: block ? "100%" : void 0,
        minWidth: block ? void 0 : 0,
        borderRadius: "var(--w-radius-button)",
        border: 0,
        background: "var(--w-ink-display)",
        color: "var(--w-bg)",
        font: "inherit",
        fontSize: "var(--w-size-caption)",
        fontWeight: 600,
        cursor: disabled ? "default" : "pointer",
        opacity: disabled ? 0.45 : 1
      },
      children
    }
  );
}
function DangerBtn({ children, onClick }) {
  return /* @__PURE__ */ (0, import_jsx_runtime16.jsx)(
    "button",
    {
      type: "button",
      onClick,
      style: {
        height: 32,
        padding: "0 10px",
        borderRadius: "var(--w-radius-button)",
        border: "1px solid var(--w-signal-edge)",
        background: "transparent",
        color: "var(--w-signal)",
        font: "inherit",
        fontSize: "var(--w-size-caption)",
        cursor: "pointer"
      },
      children
    }
  );
}

// src/screens/character/newcharacter.tsx
var import_jsx_runtime17 = __toESM(require_jsx_runtime(), 1);
var STEPS = ["\u57FA\u672C", "\u4EBA\u8BBE", "\u5F62\u8C61", "\u4E16\u754C\u89C2"];
var LAST = STEPS.length - 1;
var GENDERS = ["\u5973", "\u7537", "\u5176\u4ED6", "\u4FDD\u5BC6"];
function NewCharacter({ back }) {
  const wide = useIsWide(860);
  const [step, setStep] = (0, import_react12.useState)(0);
  const [name, setName] = (0, import_react12.useState)("");
  const [gender, setGender] = (0, import_react12.useState)("");
  const [address, setAddress] = (0, import_react12.useState)("\u4E3B\u4EBA");
  const [romance, setRomance] = (0, import_react12.useState)(true);
  const [personality, setPersonality] = (0, import_react12.useState)("");
  const [description, setDescription] = (0, import_react12.useState)("");
  const [greeting, setGreeting] = (0, import_react12.useState)("");
  const [nameError, setNameError] = (0, import_react12.useState)("");
  const [genderError, setGenderError] = (0, import_react12.useState)("");
  const isLast = step >= LAST;
  const previewName = name.trim() ? name.trim() : "\u8FD8\u6CA1\u8D77\u540D";
  const previewMeta = `${gender.trim() ? gender.trim() : "\u6027\u522B\u672A\u9009"} \xB7 ${romance ? "\u604B\u7231\u5BF9\u8C61" : "\u4F19\u4F34"} \xB7 \u79F0\u547C\u4F60\u300C${address.trim() ? address.trim() : "\u4E3B\u4EBA"}\u300D`;
  const validate = () => {
    const nextName = name.trim() ? "" : "\u540D\u5B57\u4E0D\u80FD\u7A7A\u7740\uFF1A\u5148\u7ED9 TA \u8D77\u4E2A\u540D\u5B57\u3002";
    const nextGender = gender.trim() ? "" : "\u9009\u4E00\u4E2A\u6027\u522B\uFF1A\u79F0\u547C\u4E0E\u5468\u671F\u89C4\u5219\u90FD\u4ECE\u8FD9\u91CC\u6765\u3002";
    setNameError(nextName);
    setGenderError(nextGender);
    return !nextName && !nextGender;
  };
  const next = () => {
    if (step === 0 && !validate()) return;
    if (step < LAST) setStep(step + 1);
  };
  const pick = (value) => {
    setGender(value);
    setGenderError("");
  };
  const create = () => {
    if (!validate()) {
      setStep(0);
      return;
    }
    back();
  };
  return /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(
      PageHeader,
      {
        title: "\u65B0\u5EFA\u89D2\u8272",
        eyebrow: "// NEW SUBJECT",
        onBack: back,
        right: /* @__PURE__ */ (0, import_jsx_runtime17.jsx)("span", { className: "cap", style: { fontFamily: "var(--w-font-num)" }, children: `${Math.min(step, LAST) + 1} / ${STEPS.length}` })
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)("div", { className: "screen screen--full", children: [
      /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)(Stack, { gap: 3, children: [
        /* @__PURE__ */ (0, import_jsx_runtime17.jsx)("div", { style: { display: "grid", gridTemplateColumns: "1fr 6px 1fr 6px 1fr 6px 1fr", height: 4 }, children: STEPS.map((label, index) => /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(
          "i",
          {
            "aria-hidden": "true",
            style: {
              gridColumn: index * 2 + 1,
              borderRadius: 2,
              background: step >= index ? "var(--w-signal)" : "var(--w-hairline)"
            }
          },
          label
        )) }),
        /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Eyebrow, { children: STEPS[step] }),
          /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Caption, { children: "\u968F\u65F6\u53EF\u4EE5\u56DE\u4E0A\u4E00\u6B65" })
        ] })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)(
        "div",
        {
          style: {
            display: "grid",
            gridTemplateColumns: wide ? "minmax(0, 1fr) minmax(0, 1fr)" : "minmax(0, 1fr)",
            gap: "var(--w-space-3)",
            alignItems: "start"
          },
          children: [
            /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Glass, { as: "section", tone: "card", radius: 12, style: { padding: "var(--w-pad-card)" }, children: /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)(Stack, { gap: 3, children: [
              /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)("div", { style: { display: "flex", alignItems: "baseline", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
                /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Eyebrow, { children: "// PREVIEW" }),
                /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Caption, { children: `${Math.min(step, LAST) + 1} / ${STEPS.length}` })
              ] }),
              /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(
                "div",
                {
                  style: {
                    height: 360,
                    borderRadius: 4,
                    background: "var(--w-surface-tint)",
                    display: "grid",
                    placeItems: "center",
                    textAlign: "center"
                  },
                  children: /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)("div", { style: { display: "flex", flexDirection: "column", alignItems: "center", gap: "var(--w-space-3)", padding: "var(--w-pad-card)" }, children: [
                    /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(BodySm, { children: "\u8FD8\u6CA1\u6709\u7ACB\u7ED8\u6216\u5934\u50CF" }),
                    /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Caption, { children: "\u5230\u300C\u5F62\u8C61\u300D\u90A3\u4E00\u6B65\u6311\u4E00\u5F20\uFF0C\u8FD9\u5757\u5C31\u4EAE\u8D77\u6765" })
                  ] })
                }
              ),
              /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 2 }, children: [
                /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(
                  "span",
                  {
                    style: {
                      fontSize: 26,
                      fontWeight: 600,
                      color: "var(--w-ink-display)",
                      overflow: "hidden",
                      textOverflow: "ellipsis",
                      whiteSpace: "nowrap"
                    },
                    children: previewName
                  }
                ),
                /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Caption, { children: previewMeta }),
                /* @__PURE__ */ (0, import_jsx_runtime17.jsx)("span", { style: { fontSize: "var(--w-size-small)", color: "var(--w-ink-body)", lineHeight: 1.5, marginTop: 4 }, children: personality.trim() ? personality.trim() : "\u6027\u683C\u8FD8\u6CA1\u5199" })
              ] }),
              /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Hair, {}),
              /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 1fr", columnGap: "var(--w-space-3)" }, children: [
                /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 1 }, children: [
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Eyebrow, { children: "\u7ACB\u7ED8" }),
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Caption, { children: "\u8FD8\u6CA1\u6709\u9009\u7ACB\u7ED8" })
                ] }),
                /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 1 }, children: [
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Eyebrow, { children: "\u5934\u50CF" }),
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Caption, { children: "\u8FD8\u6CA1\u6709\u5934\u50CF\uFF08\u521B\u5EFA\u65F6\u4F1A\u5148\u7528\u7ACB\u7ED8\uFF09" })
                ] })
              ] }),
              /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Caption, { children: "\u8FD9\u91CC\u53EA\u662F\u9884\u89C8\uFF1A\u6309\u300C\u521B\u5EFA\u300D\u4E4B\u540E\uFF0CTA \u624D\u4F1A\u51FA\u73B0\u5728\u89D2\u8272\u5E93\u4E0E\u5BF9\u8BDD\u91CC\u3002" })
            ] }) }),
            /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Glass, { as: "section", tone: "card", radius: 12, style: { padding: "var(--w-pad-card)" }, children: /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)(Stack, { gap: 4, children: [
              step === 0 ? /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)(Stack, { gap: 4, children: [
                /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Lead, { eyebrow: "01 \xB7 BASICS", title: "\u5148\u8BA4\u8BC6\u4E00\u4E0B", sub: "\u540D\u5B57\u4E0E\u6027\u522B\u662F\u5FC5\u586B\u7684\uFF1B\u5176\u4F59\u7559\u7A7A\u4E5F\u80FD\u521B\u5EFA\uFF0C\u4E4B\u540E\u5728\u5DE5\u4F5C\u5BA4\u91CC\u968F\u65F6\u8865\u3002" }),
                /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)(Stack, { gap: 1, children: [
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(FieldLabel, { children: "\u540D\u5B57" }),
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Field, { value: name, onChange: setName, placeholder: "\u6BD4\u5982\uFF1A\u6797\u665A" }),
                  nameError ? /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(ErrorText, { children: nameError }) : null
                ] }),
                /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)(Stack, { gap: 1, children: [
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(FieldLabel, { children: "\u6027\u522B" }),
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Chips, { children: GENDERS.map((item) => /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(ChipBtn, { label: item, on: gender === item, onClick: () => pick(item) }, item)) }),
                  genderError ? /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(ErrorText, { children: genderError }) : null,
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Caption, { children: "\u5019\u9009\u6765\u81EA\u672C\u673A\u6027\u522B\u5E93\uFF1B\u60F3\u8981\u522B\u7684\uFF0C\u53BB\u8BBE\u7F6E \u2192 \u6027\u522B\u5E93\u52A0\u4E00\u4E2A\u3002" })
                ] }),
                /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)(Stack, { gap: 1, children: [
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(FieldLabel, { children: "TA \u600E\u4E48\u79F0\u547C\u4F60" }),
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Field, { value: address, onChange: setAddress, placeholder: "\u4E3B\u4EBA / \u54E5\u54E5 / \u540D\u5B57" })
                ] }),
                /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(
                  SwitchLine,
                  {
                    title: "\u604B\u7231\u5BF9\u8C61",
                    desc: "\u5173\u6389\u540E TA \u4E0D\u4F1A\u4E3B\u52A8\u63A8\u8FDB\u5173\u7CFB\uFF0C\u5468\u671F\u4E0E\u4EB2\u5BC6\u5185\u5BB9\u4E5F\u4E0D\u8FDB\u63D0\u793A\u8BCD\u3002",
                    checked: romance,
                    onChange: setRomance
                  }
                )
              ] }) : null,
              step === 1 ? /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)(Stack, { gap: 4, children: [
                /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Lead, { eyebrow: "02 \xB7 PERSONA", title: "TA \u662F\u4EC0\u4E48\u6837\u7684\u4EBA", sub: "\u8FD9\u4E09\u6BB5\u76F4\u63A5\u8FDB\u63D0\u793A\u8BCD\u3002\u5199\u5177\u4F53\u4E00\u70B9\uFF0CTA \u56DE\u8BDD\u65F6\u624D\u6709\u4E1C\u897F\u53EF\u6293\u3002" }),
                /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)(Stack, { gap: 1, children: [
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(FieldLabel, { children: "\u6027\u683C" }),
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(
                    Field,
                    {
                      value: personality,
                      onChange: setPersonality,
                      placeholder: "\u8BF4\u8BDD\u65B9\u5F0F\u4E0E\u813E\u6C14\uFF1A\u5634\u786C\u5FC3\u8F6F\u3001\u6162\u70ED\u3001\u7231\u5F00\u73A9\u7B11\u2026\u2026",
                      multiline: true
                    }
                  )
                ] }),
                /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)(Stack, { gap: 1, children: [
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(FieldLabel, { children: "\u7B80\u4ECB / \u524D\u60C5" }),
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(
                    Field,
                    {
                      value: description,
                      onChange: setDescription,
                      placeholder: "\u4F60\u4EEC\u4E4B\u95F4\u53D1\u751F\u8FC7\u4EC0\u4E48\u3001TA \u73B0\u5728\u8FC7\u7740\u600E\u6837\u7684\u65E5\u5B50",
                      multiline: true
                    }
                  )
                ] }),
                /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)(Stack, { gap: 1, children: [
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(FieldLabel, { children: "\u5F00\u573A\u767D" }),
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Field, { value: greeting, onChange: setGreeting, placeholder: "TA \u4E0A\u7EBF\u7B2C\u4E00\u53E5\u8BF4\u4EC0\u4E48", multiline: true })
                ] })
              ] }) : null,
              step === 2 ? /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)(Stack, { gap: 4, children: [
                /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(
                  Lead,
                  {
                    eyebrow: "03 \xB7 IMAGE",
                    title: "\u8BA9 TA \u7AD9\u5230\u5149\u91CC",
                    sub: "\u7ACB\u7ED8\u5148\u652F\u6301\u4E00\u5F20\u9ED8\u8BA4\u8868\u60C5\uFF1B\u5934\u50CF\u4F1A\u7528\u5728\u5217\u8868\u3001\u5BF9\u8BDD\u4E0E\u624B\u673A\u91CC\u3002\u4E24\u6837\u90FD\u80FD\u4E4B\u540E\u5728\u5DE5\u4F5C\u5BA4\u6362\u3002"
                  }
                ),
                /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)(Well2, { children: [
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
                    /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(BodySm, { children: "\u7ACB\u7ED8" }),
                    /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Caption, { children: "\u8FD8\u6CA1\u6709\u9009\u7ACB\u7ED8" })
                  ] }),
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Actions, { children: /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(GlassButton, { size: "sm", children: "\u9009\u4E00\u5F20\u7ACB\u7ED8" }) }),
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Caption, { children: "\u8FD9\u5F20\u4F1A\u8BB0\u6210 default/normal\uFF1B\u8981\u66F4\u591A\u8868\u60C5\uFF0C\u521B\u5EFA\u540E\u53BB\u5DE5\u4F5C\u5BA4\u8865\u3002" })
                ] }),
                /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)(Well2, { children: [
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
                    /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(BodySm, { children: "\u5934\u50CF" }),
                    /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Caption, { children: "\u8FD8\u6CA1\u6709\u5934\u50CF\uFF08\u521B\u5EFA\u65F6\u4F1A\u5148\u7528\u7ACB\u7ED8\uFF09" })
                  ] }),
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)(Actions, { children: [
                    /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(PrimaryBtn, { children: "\u9009\u5934\u50CF\u5E76\u88C1\u526A" }),
                    /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(GlassButton, { size: "sm", children: "\u76F4\u63A5\u7528\u8FD9\u5F20\u56FE" })
                  ] }),
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Caption, { children: "\u88C1\u526A\u4F1A\u6846\u6210\u65B9\u8138\uFF0C\u5B58\u6210 512\xD7512\uFF1B\u76F4\u63A5\u7528\u539F\u56FE\u4F1A\u4FDD\u7559\u539F\u5C3A\u5BF8\u3002" })
                ] }),
                /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Caption, { children: "\u6CA1\u6709\u56FE\u4E5F\u80FD\u521B\u5EFA\uFF1ATA \u4F1A\u5148\u4EE5\u59D3\u540D\u9996\u5B57\u51FA\u73B0\u5728\u5217\u8868\u91CC\uFF0C\u4E4B\u540E\u968F\u65F6\u8865\u56FE\u3002" })
              ] }) : null,
              step === 3 ? /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)(Stack, { gap: 4, children: [
                /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Lead, { eyebrow: "04 \xB7 WORLD", title: "TA \u4ECE\u54EA\u4E2A\u4E16\u754C\u6765", sub: "\u8FD9\u4E00\u6B65\u53EF\u4EE5\u6574\u6B65\u8DF3\u8FC7\uFF1A\u4E0D\u7ED1\u4E16\u754C\u4E66\u4E5F\u80FD\u521B\u5EFA\uFF0C\u4E4B\u540E\u5728\u5DE5\u4F5C\u5BA4\u91CC\u8865\u3002" }),
                /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)(Stack, { gap: 2, children: [
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
                    /* @__PURE__ */ (0, import_jsx_runtime17.jsx)("span", { style: { fontSize: "var(--w-size-subhead)", color: "var(--w-ink-display)" }, children: "\u4E16\u754C\u4E66\uFF08\u53EF\u591A\u9009\uFF09" }),
                    /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Caption, { children: "\u672A\u9009\u62E9" })
                  ] }),
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Glass, { as: "div", tone: "card", radius: 12, style: { padding: 0 }, children: /* @__PURE__ */ (0, import_jsx_runtime17.jsx)("div", { style: { padding: "var(--w-space-2) var(--w-pad-card)" }, children: /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Caption, { children: "\u8FD8\u6CA1\u6709\u4E16\u754C\u4E66\u3002\u53EF\u4EE5\u5148\u53BB\u300C\u4E16\u754C\u4E66\u300D\u624B\u5199\u4E00\u4EFD\uFF0C\u6216\u8005\u8BA9 AI \u9020\u4E00\u4E2A\uFF1B\u8FD9\u4E00\u6B65\u8DF3\u8FC7\u4E5F\u4E0D\u5F71\u54CD\u521B\u5EFA\u3002" }) }) }),
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Caption, { children: "\u4E0D\u7ED1\u5B9A\u4E16\u754C\u4E66\uFF08\u53EF\u4EE5\u8DF3\u8FC7\uFF09" })
                ] }),
                /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)(Well2, { children: [
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
                    /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 2 }, children: [
                      /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(BodySm, { children: "\u8BED\u97F3\u5F15\u64CE" }),
                      /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Caption, { children: "\u7528\u54EA\u5957\u58F0\u97F3\u8BF4\u8BDD" })
                    ] }),
                    /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Caption, { children: "\u8DDF\u968F\u5168\u5C40" })
                  ] }),
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Hair, {}),
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
                    /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 2 }, children: [
                      /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(BodySm, { children: "AI \u6A21\u578B" }),
                      /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Caption, { children: "\u7528\u54EA\u4E00\u6863\u6A21\u578B\u56DE\u8BDD" })
                    ] }),
                    /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Caption, { children: "\u8DDF\u968F\u5168\u5C40" })
                  ] }),
                  /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Caption, { children: "\u60F3\u7ED9 TA \u5355\u72EC\u6307\u5B9A\uFF0C\u521B\u5EFA\u540E\u5728\u5DE5\u4F5C\u5BA4\u7684\u300C\u8BB0\u5FC6 / \u7D20\u6750\u300D\u91CC\u6539\uFF1B\u8FD9\u91CC\u5148\u8DDF\u7740\u5168\u5C40\u3002" })
                ] })
              ] }) : null
            ] }) })
          ]
        }
      )
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime17.jsx)("div", { style: { position: "sticky", bottom: 0, zIndex: 5, borderTop: "1px solid var(--w-hairline)" }, children: /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Glass, { as: "div", tone: "bar", radius: 0, children: /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)(
      "div",
      {
        style: {
          display: "grid",
          gridTemplateColumns: "auto minmax(0,1fr) auto",
          alignItems: "center",
          gap: "var(--w-space-3)",
          padding: "var(--w-space-2) var(--w-pad-page)"
        },
        children: [
          step > 0 ? /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(
            "button",
            {
              type: "button",
              onClick: () => setStep(step - 1),
              style: {
                border: 0,
                background: "none",
                padding: "2px 6px",
                font: "inherit",
                fontSize: "var(--w-size-caption)",
                color: "var(--w-ink-muted)",
                cursor: "pointer"
              },
              children: "\u2190 \u4E0A\u4E00\u6B65"
            }
          ) : /* @__PURE__ */ (0, import_jsx_runtime17.jsx)("span", {}),
          /* @__PURE__ */ (0, import_jsx_runtime17.jsx)("span", { style: { fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)", textAlign: "center" }, children: STEPS[step] }),
          /* @__PURE__ */ (0, import_jsx_runtime17.jsx)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-3)" }, children: isLast ? /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(PrimaryBtn, { onClick: create, children: "\u521B\u5EFA\u89D2\u8272" }) : /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(PrimaryBtn, { onClick: next, children: "\u4E0B\u4E00\u6B65 \u2192" }) })
        ]
      }
    ) }) })
  ] });
}
function FieldLabel({ children }) {
  return /* @__PURE__ */ (0, import_jsx_runtime17.jsx)("span", { style: { fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)", margin: "0 0 -1px 2px" }, children });
}
function ErrorText({ children }) {
  return /* @__PURE__ */ (0, import_jsx_runtime17.jsx)("span", { style: { fontSize: "var(--w-size-caption)", color: "var(--w-signal)", margin: "4px 0 0 2px", lineHeight: 1.5 }, children });
}
function Lead({ eyebrow: eyebrow2, title: title2, sub }) {
  return /* @__PURE__ */ (0, import_jsx_runtime17.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-1)" }, children: [
    /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Eyebrow, { children: eyebrow2 }),
    /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(
      "h2",
      {
        style: {
          margin: 0,
          fontSize: "var(--w-size-display)",
          fontWeight: 600,
          color: "var(--w-ink-display)",
          lineHeight: 1.35
        },
        children: title2
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime17.jsx)(Note, { children: sub })
  ] });
}

// src/screens/character/roster.tsx
var import_react13 = __toESM(require_react(), 1);
var import_jsx_runtime18 = __toESM(require_jsx_runtime(), 1);
var SEED = [
  {
    id: "seed",
    name: "\u5C0F\u96E8",
    personality: "\u6E29\u67D4\u53EF\u7231",
    description: "\u521A\u521A\u8BDE\u751F\u7684\u4F19\u4F34\uFF0C\u4E00\u5207\u90FD\u662F\u5D2D\u65B0\u7684\u5F00\u59CB\u3002",
    saveCount: 0,
    cellCount: 0,
    affection: 0
  }
];
var levelOf = (points) => points <= 0 ? 1 : Math.min(10, Math.floor((points - 1) / 100) + 1);
function Roster({ navigate, back }) {
  const wide = useIsWide(720);
  const [items] = (0, import_react13.useState)(SEED);
  const [selectedId, setSelectedId] = (0, import_react13.useState)(SEED[0]?.id ?? "");
  const selected = items.find((item) => item.id === selectedId) ?? null;
  const countLabel = items.length === 0 ? "\u8FD8\u6CA1\u6709\u4EBA" : `${items.length} \u4E2A\u89D2\u8272`;
  const enter = () => {
    if (selected) navigate(`/studio?id=${encodeURIComponent(selected.id)}`);
  };
  return /* @__PURE__ */ (0, import_jsx_runtime18.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(
      PageHeader,
      {
        title: "\u89D2\u8272\u5E93",
        eyebrow: "// ROSTER",
        onBack: back,
        right: /* @__PURE__ */ (0, import_jsx_runtime18.jsxs)(import_jsx_runtime18.Fragment, { children: [
          /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(GlassButton, { size: "sm", children: "\u76D1\u6D4B" }),
          /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(
            "span",
            {
              className: "cap",
              style: {
                padding: "6px 10px",
                borderRadius: 4,
                background: "var(--w-surface-tint)",
                display: wide ? void 0 : "none"
              },
              children: "AI \u4E5F\u4F1A\u7528\u5230\u5176\u4ED6\u89D2\u8272"
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(PrimaryBtn, { onClick: () => navigate("/newcharacter"), children: "\uFF0B \u65B0\u5EFA\u89D2\u8272" })
        ] })
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime18.jsx)("div", { className: "screen screen--full", children: /* @__PURE__ */ (0, import_jsx_runtime18.jsxs)(
      "div",
      {
        style: {
          display: "grid",
          gridTemplateColumns: wide ? "372px minmax(0, 1fr)" : "minmax(0, 1fr)",
          gap: "var(--w-space-3)",
          alignItems: "stretch"
        },
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(Glass, { as: "section", tone: "card", radius: 12, style: { padding: 0, minHeight: wide ? 0 : 300 }, children: /* @__PURE__ */ (0, import_jsx_runtime18.jsxs)("div", { style: { display: "flex", flexDirection: "column", minHeight: 0 }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime18.jsxs)(
              "div",
              {
                style: {
                  display: "flex",
                  alignItems: "flex-end",
                  justifyContent: "space-between",
                  gap: "var(--w-space-2)",
                  padding: "var(--w-space-2) var(--w-pad-card)"
                },
                children: [
                  /* @__PURE__ */ (0, import_jsx_runtime18.jsxs)("div", { style: { display: "flex", flexDirection: "column" }, children: [
                    /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(Eyebrow, { children: "// ALL SUBJECTS" }),
                    /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(PaneTitle, { children: "\u8FD9\u91CC\u7684\u4EBA" })
                  ] }),
                  /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(Caption, { children: countLabel })
                ]
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(Hair, {}),
            /* @__PURE__ */ (0, import_jsx_runtime18.jsx)("div", { style: { flex: 1, minHeight: 0, overflowY: "auto" }, children: items.length === 0 ? /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(
              Empty,
              {
                title: "\u89D2\u8272\u5E93\u8FD8\u662F\u7A7A\u7684",
                reason: "\u540D\u518C\u91CC\u8FD8\u6CA1\u6709\u4EFB\u4F55\u4EBA",
                hint: "\u5728\u89D2\u8272\u9009\u62E9\u9875\u65B0\u5EFA\u6216\u5BFC\u5165\u89D2\u8272\uFF0C\u65B0\u89D2\u8272\u4F1A\u51FA\u73B0\u5728\u8FD9\u91CC"
              }
            ) : items.map((item) => {
              const on = item.id === selectedId;
              return /* @__PURE__ */ (0, import_jsx_runtime18.jsxs)("div", { style: { position: "relative" }, children: [
                on ? /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(
                  "i",
                  {
                    "aria-hidden": "true",
                    style: {
                      position: "absolute",
                      left: 0,
                      top: 0,
                      bottom: 0,
                      width: 2,
                      background: "var(--w-signal)"
                    }
                  }
                ) : null,
                /* @__PURE__ */ (0, import_jsx_runtime18.jsxs)(
                  "div",
                  {
                    role: "button",
                    tabIndex: 0,
                    onClick: () => setSelectedId(item.id),
                    onKeyDown: (event) => {
                      if (event.key === "Enter" || event.key === " ") setSelectedId(item.id);
                    },
                    style: {
                      display: "flex",
                      alignItems: "center",
                      gap: "var(--w-space-3)",
                      width: "100%",
                      padding: "var(--w-space-2) var(--w-pad-card)",
                      border: 0,
                      background: "none",
                      font: "inherit",
                      textAlign: "left",
                      cursor: "pointer"
                    },
                    children: [
                      /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(
                        "span",
                        {
                          style: {
                            width: 46,
                            height: 46,
                            flex: "none",
                            display: "grid",
                            placeItems: "center",
                            borderRadius: 4,
                            background: "var(--w-surface-tint)",
                            color: "var(--w-ink-display)",
                            fontSize: "var(--w-size-body)"
                          },
                          children: item.name.slice(0, 1) || "?"
                        }
                      ),
                      /* @__PURE__ */ (0, import_jsx_runtime18.jsxs)("span", { style: { display: "flex", flexDirection: "column", gap: 1, minWidth: 0, flex: 1 }, children: [
                        /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(
                          "span",
                          {
                            style: {
                              fontSize: "var(--w-size-body)",
                              color: "var(--w-ink-display)",
                              overflow: "hidden",
                              textOverflow: "ellipsis",
                              whiteSpace: "nowrap"
                            },
                            children: item.name
                          }
                        ),
                        /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(
                          "span",
                          {
                            style: {
                              fontSize: "var(--w-size-caption)",
                              color: "var(--w-ink-muted)",
                              overflow: "hidden",
                              textOverflow: "ellipsis",
                              whiteSpace: "nowrap"
                            },
                            children: item.personality
                          }
                        )
                      ] }),
                      /* @__PURE__ */ (0, import_jsx_runtime18.jsxs)("span", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-1)" }, children: [
                        /* @__PURE__ */ (0, import_jsx_runtime18.jsx)("span", { onClick: (event) => event.stopPropagation(), children: /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(TextBtn, { children: "\u5BFC\u51FA" }) }),
                        /* @__PURE__ */ (0, import_jsx_runtime18.jsx)("span", { style: { color: "var(--w-ink-faint)", fontSize: 18 }, children: "\u203A" })
                      ] })
                    ]
                  }
                ),
                /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(Hair, {})
              ] }, item.id);
            }) })
          ] }) }),
          /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(Glass, { as: "section", tone: "card", radius: 12, style: { padding: 0 }, children: selected ? /* @__PURE__ */ (0, import_jsx_runtime18.jsxs)("div", { style: { display: "flex", flexDirection: "column", minHeight: "100%" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime18.jsxs)(
              "div",
              {
                style: {
                  position: "relative",
                  minHeight: 300,
                  flex: 1,
                  borderRadius: "12px 12px 0 0",
                  background: "var(--w-surface-tint)",
                  display: "grid",
                  placeItems: "center",
                  overflow: "hidden"
                },
                children: [
                  /* @__PURE__ */ (0, import_jsx_runtime18.jsx)("span", { style: { position: "absolute", left: 16, top: 16 }, children: /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(Eyebrow, { children: "// SUBJECT" }) }),
                  /* @__PURE__ */ (0, import_jsx_runtime18.jsxs)("div", { style: { display: "flex", flexDirection: "column", alignItems: "center", gap: "var(--w-space-3)" }, children: [
                    /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(BodySm, { children: "\u8FD8\u6CA1\u6709\u7ACB\u7ED8" }),
                    /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(Caption, { children: "\u8FDB TA \u7684\u5DE5\u4F5C\u5BA4\u751F\u6210\u4E00\u5957\uFF0C\u6216\u4ECE zip \u8865\u8FDB\u6765" })
                  ] })
                ]
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime18.jsx)("div", { style: { padding: "var(--w-pad-card)" }, children: /* @__PURE__ */ (0, import_jsx_runtime18.jsxs)(Stack, { gap: 3, children: [
              /* @__PURE__ */ (0, import_jsx_runtime18.jsxs)("div", { style: { display: "flex", alignItems: "flex-start", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
                /* @__PURE__ */ (0, import_jsx_runtime18.jsxs)("div", { style: { display: "flex", flexDirection: "column" }, children: [
                  /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(Eyebrow, { children: "// SUBJECT" }),
                  /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(
                    "h2",
                    {
                      style: {
                        margin: 0,
                        fontSize: "var(--w-size-display)",
                        fontWeight: 600,
                        letterSpacing: "-0.5px",
                        color: "var(--w-ink-display)",
                        lineHeight: 1.3
                      },
                      children: selected.name
                    }
                  )
                ] }),
                /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(
                  "span",
                  {
                    style: {
                      padding: "3px 8px",
                      borderRadius: 4,
                      background: "var(--w-surface-tint)",
                      fontSize: "var(--w-size-caption)",
                      color: "var(--w-ink-muted)",
                      whiteSpace: "nowrap"
                    },
                    children: selected.saveCount > 0 ? `\u5728 ${selected.saveCount} \u4EFD\u5B58\u6863\u91CC` : "\u8FD8\u6CA1\u8FDB\u8FC7\u5B58\u6863"
                  }
                )
              ] }),
              /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(BodySm, { children: selected.personality || "\u8FD8\u6CA1\u5199\u6027\u683C" }),
              /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(Caption, { children: selected.description || "\u8FD8\u6CA1\u6709\u80CC\u666F\u6545\u4E8B\uFF1A\u8FDB\u5DE5\u4F5C\u5BA4\u8865\u4E00\u6BB5\uFF0CAI \u624D\u4F1A\u7167\u7740\u8FD9\u4E2A\u4EBA\u56DE\u8BDD\u3002" }),
              /* @__PURE__ */ (0, import_jsx_runtime18.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
                /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(Caption, { children: "\u8FD8\u6CA1\u6709\u4E13\u5C5E\u80CC\u666F\uFF1A\u70B9\u300C\u6362\u80CC\u666F\u300D\u9009\u4E00\u5F20\u56FE / GIF / \u89C6\u9891\u3002" }),
                /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(GlassButton, { size: "sm", children: "\u6362\u80CC\u666F" })
              ] }),
              /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(Hair, {}),
              /* @__PURE__ */ (0, import_jsx_runtime18.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 1px 1fr 1px 1fr", columnGap: "var(--w-space-2)" }, children: [
                /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(
                  Reading,
                  {
                    eyebrow: "AFFECTION",
                    value: `Lv.${levelOf(selected.affection)}`,
                    note: `\u597D\u611F \xB7 ${Math.max(0, Math.min(1e3, selected.affection))}/1000`
                  }
                ),
                /* @__PURE__ */ (0, import_jsx_runtime18.jsx)("i", { style: { background: "var(--w-hairline)" } }),
                /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(Reading, { eyebrow: "SPRITES", value: String(selected.cellCount), note: "\u5F20\u7ACB\u7ED8" }),
                /* @__PURE__ */ (0, import_jsx_runtime18.jsx)("i", { style: { background: "var(--w-hairline)" } }),
                /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(Reading, { eyebrow: "SAVES", value: String(selected.saveCount), note: "\u4E2A\u5B58\u6863" })
              ] }),
              /* @__PURE__ */ (0, import_jsx_runtime18.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "minmax(0,1fr) auto", columnGap: "var(--w-space-3)", alignItems: "center" }, children: [
                /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(PrimaryBtn, { block: true, onClick: enter, children: "\u8FDB\u5165 TA \u7684\u5DE5\u4F5C\u5BA4  \u2192" }),
                /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(TextBtn, { children: "\u5220\u9664" })
              ] })
            ] }) })
          ] }) : /* @__PURE__ */ (0, import_jsx_runtime18.jsxs)(
            "div",
            {
              style: {
                display: "flex",
                flexDirection: "column",
                alignItems: "center",
                justifyContent: "center",
                gap: "var(--w-space-1)",
                padding: "var(--w-pad-card)",
                minHeight: 300,
                textAlign: "center"
              },
              children: [
                /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(Eyebrow, { children: "// NO SUBJECTS" }),
                /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(PaneTitle, { children: "\u8FD8\u6CA1\u6709\u4EBA\u53EF\u4EE5\u7AD9\u4E0A\u8FD9\u5757\u821E\u53F0" }),
                /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(Caption, { children: "\u53BB\u89D2\u8272\u9009\u62E9\u9875\u65B0\u5EFA\u6216\u5BFC\u5165\u4E00\u4E2A\u89D2\u8272\uFF0C\u56DE\u6765\u5C31\u80FD\u5728\u8FD9\u91CC\u770B\u89C1 TA\u3002" })
              ]
            }
          ) })
        ]
      }
    ) })
  ] });
}
function Reading({ eyebrow: eyebrow2, value, note }) {
  return /* @__PURE__ */ (0, import_jsx_runtime18.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 1 }, children: [
    /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(Eyebrow, { children: eyebrow2 }),
    /* @__PURE__ */ (0, import_jsx_runtime18.jsxs)("div", { style: { display: "flex", alignItems: "flex-end", gap: "var(--w-space-1)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime18.jsx)(
        "span",
        {
          style: {
            fontFamily: "var(--w-font-num)",
            fontSize: 24,
            color: "var(--w-ink-display)",
            fontVariantNumeric: "tabular-nums"
          },
          children: value
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime18.jsx)("span", { style: { fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)", paddingBottom: 4 }, children: note })
    ] })
  ] });
}

// src/screens/character/studio.tsx
var import_react14 = __toESM(require_react(), 1);
var import_jsx_runtime19 = __toESM(require_jsx_runtime(), 1);
var TABS2 = [
  { key: "set", label: "01 \u8BBE\u5B9A" },
  { key: "state", label: "02 \u72B6\u6001" },
  { key: "art", label: "03 \u7D20\u6750" },
  { key: "mem", label: "04 \u8BB0\u5FC6" },
  { key: "link", label: "05 \u5173\u8054" },
  { key: "talk", label: "06 \u8BF4\u53E5\u8BDD" },
  { key: "cg", label: "07 CG" }
];
var GENDERS2 = ["\u5973", "\u7537", "\u5176\u4ED6", "\u4FDD\u5BC6"];
var MOODS = [
  { key: "normal", label: "\u5E73\u5E38" },
  { key: "happy", label: "\u5F00\u5FC3" },
  { key: "shy", label: "\u5BB3\u7F9E" },
  { key: "sad", label: "\u4F4E\u843D" },
  { key: "angry", label: "\u95F9\u813E\u6C14" },
  { key: "tired", label: "\u75B2\u60EB" },
  { key: "worried", label: "\u4E0D\u5B89" }
];
var OUTFITS = [{ key: "default", label: "\u9ED8\u8BA4\u88C5\u626E" }];
var EMOTIONS = [{ key: "normal", label: "normal" }];
var LAYERS = [
  { name: "\u5DE5\u4F5C\u8BB0\u5FC6", detail: "\u672C\u8F6E\u5BF9\u8BDD\u7684\u4E0A\u4E0B\u6587\uFF0C\u957F\u5EA6\u7531\u300C\u8BB0\u5FC6\u6761\u6570\u300D\u51B3\u5B9A\uFF0C\u8D85\u51FA\u5373\u4E22\u5F03" },
  { name: "\u4EBA\u7C7B\u8BB0\u5FC6", detail: "\u53EA\u8BB0\u4E8B\u4EF6\u3001\u5370\u8C61\u3001\u91CD\u8981\u65F6\u523B\u4E0E\u5355\u5F20\u7167\u7247\uFF1B\u91CD\u8981\u7684\u5FD8\u5F97\u6162\uFF0C\u7410\u788E\u7684\u81EA\u7136\u6DE1\u5FD8" },
  { name: "\u5173\u7CFB\u7F51", detail: "\u8BCD\u4E0E\u8BCD\u7684\u8054\u7CFB\uFF0C\u8D1F\u8D23\u7531\u4E00\u4E2A\u8BCD\u60F3\u8D77\u53E6\u4E00\u4EF6\u4E8B" },
  { name: "\u957F\u65F6\u8BB0\u5FC6\u5E93", detail: "\u6240\u6709\u5BF9\u8BDD\u4E0E\u597D\u611F\u8BB0\u5F55\u90FD\u5728\uFF0C\u53EA\u589E\u4E0D\u5220\uFF0C\u9760\u68C0\u7D22\u5DE5\u5177\u6309\u9700\u7FFB" },
  { name: "\u6863\u6848\u5C42", detail: "\u65E5\u8BB0\u4E0E\u56DE\u5FC6\u5F55\uFF0C\u662F\u6574\u7406\u7ED9\u4EBA\u770B\u7684\u7A3F\u5B50" }
];
var VOICE_ENGINES = [
  { key: "", label: "\u8DDF\u968F\u5168\u5C40" },
  { key: "system", label: "\u7CFB\u7EDF\u8BED\u97F3" },
  { key: "api", label: "\u5916\u90E8 TTS \u63A5\u53E3" },
  { key: "sovits", label: "GPT-SoVITS" }
];
var VOICE_API_MODES = [
  { key: "", label: "\u8DDF\u968F\u5168\u5C40" },
  { key: "openai", label: "OpenAI \u517C\u5BB9" },
  { key: "sovits", label: "GPT-SoVITS" }
];
var EXISTING = {
  name: "\u5C0F\u96E8",
  nickname: "",
  userAddress: "\u4E3B\u4EBA",
  gender: "\u672A\u77E5",
  personality: "\u6E29\u67D4\u53EF\u7231",
  description: "\u521A\u521A\u8BDE\u751F\u7684\u4F19\u4F34\uFF0C\u4E00\u5207\u90FD\u662F\u5D2D\u65B0\u7684\u5F00\u59CB\u3002",
  greeting: "",
  moral: "",
  life: "",
  biography: "",
  political: "",
  ideology: "",
  deepMemories: "",
  trauma: "",
  age: "",
  birthday: "",
  naturalAging: true,
  romance: true,
  affection: 0,
  trust: 0,
  energy: 100,
  libido: 20,
  libidoLocked: false,
  libidoLockValue: 20,
  affectionPoints: "",
  location: "home",
  mood: "normal",
  cycleTracked: false,
  cycleDay: "0",
  cyclePhase: "",
  crossSave: true,
  ovMemoryTurns: "",
  ovContextLimit: "",
  ovTemperature: "",
  ovWindowDays: "",
  ovRecall: "",
  voiceEnabled: true,
  voiceEngine: "",
  voiceName: "",
  voiceExtra: "",
  voiceApiUrl: "",
  voiceApiKey: "",
  voiceApiMode: "",
  voiceTtsModel: "",
  allowed: true,
  duplicateName: "\u5C0F\u96E8\uFF08\u526F\u672C\uFF09"
};
var NEW_DRAFT = {
  ...EXISTING,
  name: "",
  nickname: "",
  gender: "",
  greeting: "",
  duplicateName: "\uFF08\u526F\u672C\uFF09"
};
function Studio({ query, navigate, back }) {
  const wide = useIsWide(820);
  const startNew = query.get("new") === "1";
  const [isNew, setIsNew] = (0, import_react14.useState)(startNew);
  const [f, setT] = (0, import_react14.useState)(startNew ? NEW_DRAFT : EXISTING);
  const [changed, setChanged] = (0, import_react14.useState)([]);
  const [status, setStatus] = (0, import_react14.useState)(
    startNew ? "\u5148\u586B\u59D3\u540D\u548C\u6027\u522B\uFF0C\u5176\u4F59\u90FD\u80FD\u4E4B\u540E\u518D\u8865\u2014\u2014\u6309\u300C\u5EFA\u597D\u89D2\u8272\u300D\u624D\u4F1A\u8FDB\u540D\u518C\u3002" : ""
  );
  const [tab, setTab] = (0, import_react14.useState)("set");
  const [advancedOpen, setAdvancedOpen] = (0, import_react14.useState)(false);
  const [facingLeft, setFacingLeft] = (0, import_react14.useState)(false);
  const [talkInput, setTalkInput] = (0, import_react14.useState)("");
  const [talkLines, setTalkLines] = (0, import_react14.useState)([]);
  const [talkHint, setTalkHint] = (0, import_react14.useState)("");
  const [nextLineId, setNextLineId] = (0, import_react14.useState)(1);
  const dirty = changed.length > 0;
  const saveLabel = isNew ? "\u5EFA\u597D\u89D2\u8272" : "\u4FDD\u5B58";
  const headline = isNew ? "\u65B0\u5EFA\u89D2\u8272" : f.nickname.trim() ? `${f.name}\xB7${f.nickname}` : f.name;
  const metaLine = isNew ? "\u5148\u8D77\u4E2A\u540D\u5B57\u3001\u9009\u4E2A\u6027\u522B\uFF0C\u5176\u4F59\u90FD\u80FD\u4E4B\u540E\u8865" : `${f.gender} \xB7 \u53EB\u5BF9\u65B9\u300C${f.userAddress}\u300D \xB7 ${f.romance ? "\u604B\u7231\u5BF9\u8C61" : "\u4E0D\u604B\u7231"}`;
  const previewCaption = `${OUTFITS[0].label} \xB7 normal \xB7 ${facingLeft ? "\u671D\u5DE6" : "\u671D\u53F3"}`;
  const set = (key, value, label) => {
    setT((prev) => ({ ...prev, [key]: value }));
    setChanged((list) => list.includes(label) ? list : [...list, label]);
  };
  const save = () => {
    if (!dirty) {
      setStatus("\u6CA1\u6709\u8981\u4FDD\u5B58\u7684\u6539\u52A8\u3002");
      return;
    }
    if (isNew && !f.name.trim()) {
      setStatus("\u5148\u7ED9 TA \u8D77\u4E2A\u540D\u5B57\u2014\u2014\u6CA1\u6709\u540D\u5B57\u8FDB\u4E0D\u4E86\u540D\u518C\u3002");
      return;
    }
    if (isNew && !f.gender.trim()) {
      setStatus("\u6027\u522B\u4E5F\u6311\u4E00\u9897\uFF08\u60F3\u8981\u522B\u7684\uFF0C\u8BBE\u7F6E \u2192 \u6027\u522B\u5E93\u91CC\u52A0\u4E00\u4E2A\uFF0C\u8FD9\u91CC\u7ACB\u523B\u591A\u4E00\u9897\uFF09\u3002");
      return;
    }
    const justCreated = isNew;
    setIsNew(false);
    setChanged([]);
    setStatus(
      justCreated ? `\u300C${f.name.trim()}\u300D\u5DF2\u7ECF\u8FDB\u540D\u518C\u3002\u4E4B\u540E\u6539\u54EA\u513F\uFF0C\u8FD8\u6309\u8FD9\u4E00\u9897\u6309\u94AE\u3002` : `\u5DF2\u4FDD\u5B58\u300C${f.name.trim()}\u300D\u7684\u4E00\u5207\u6539\u52A8\u3002`
    );
  };
  const revert = () => {
    if (isNew) {
      setT(NEW_DRAFT);
      setChanged([]);
      setStatus("\u6539\u52A8\u5DF2\u7ECF\u653E\u5F03\uFF0C\u56DE\u5230\u7A7A\u767D\u90A3\u4E00\u5F20\u3002");
      return;
    }
    setT(EXISTING);
    setChanged([]);
    setStatus("\u6539\u52A8\u5DF2\u7ECF\u653E\u5F03\uFF0C\u56DE\u5230\u76D8\u4E0A\u90A3\u4E00\u7248\u3002");
  };
  const close = () => {
    if (dirty) {
      const leave = window.confirm(
        `\u6539\u52A8\u8FD8\u6CA1\u4FDD\u5B58

\u300C${headline}\u300D\u8FD9\u4E00\u5C4F\u4E0A\u6709\u6CA1\u4FDD\u5B58\u7684\u6539\u52A8\u3002
\u79BB\u5F00\u5C31\u4F1A\u4E22\u6389\u5B83\u4EEC\uFF0C\u76D8\u4E0A\u8FD8\u662F\u539F\u6765\u90A3\u4E00\u7248\u3002`
      );
      if (!leave) return;
    }
    back();
  };
  const resetSubProfile = () => {
    const yes = window.confirm(
      `\u526F\u8BBE\u5B9A\u56DE\u6B63

\u628A\u300C${f.name.trim()}\u300D\u7684\u526F\u8BBE\u5B9A\u9000\u56DE\u4E3B\u8BBE\u5B9A\u7684\u526F\u672C\uFF1F\u8FD9\u4F1A\u6E05\u7A7A\u7D2F\u79EF\u7684\u5F53\u4E0B\u5FC3\u5883\u4E0E\u5FC3\u4E8B\uFF0C\u4E3B\u8BBE\u5B9A\u4E0D\u53D7\u5F71\u54CD\u3002`
    );
    if (!yes) return;
    setStatus("\u526F\u8BBE\u5B9A\u5DF2\u56DE\u6B63\u5230\u4E3B\u8BBE\u5B9A\u3002");
  };
  const talkSend = () => {
    const text = talkInput.trim();
    if (!text) {
      setTalkHint("\u5148\u5199\u4E00\u53E5\u8BDD\u2014\u2014\u6BD4\u5982\u300C\u4ECA\u5929\u600E\u4E48\u8FD9\u4E48\u65E9\u300D\u3002");
      return;
    }
    setTalkLines((lines) => [...lines, { id: nextLineId, who: "\u4F60", text, fromMe: true }]);
    setNextLineId(nextLineId + 1);
    setTalkInput("");
    setTalkHint("");
    setStatus("TA \u6CA1\u63A5\u8BDD\u2014\u2014\u8FD9\u4E00\u53E5\u8BD5\u8BDD\u6CA1\u6210\u529F\u3002");
  };
  const talkClear = () => {
    setTalkLines([]);
    setTalkHint("\u8FD9\u51E0\u53E5\u64E6\u6389\u4E86\u2014\u2014\u672C\u6765\u4E5F\u6CA1\u5199\u8FDB TA \u7684\u8BB0\u5FC6\u3002");
  };
  const ctx = {
    f,
    set,
    say: setStatus,
    advancedOpen,
    toggleAdvanced: () => setAdvancedOpen(!advancedOpen),
    navigate,
    resetSubProfile,
    facingLeft,
    setFacing: (next) => {
      setFacingLeft(next);
      setStatus((next ? "TA \u73B0\u5728\u671D\u5DE6\u7AD9" : "TA \u73B0\u5728\u671D\u53F3\u7AD9") + "\uFF08\u8FD8\u6CA1\u4FDD\u5B58\uFF09");
    },
    talkInput,
    setTalkInput,
    talkLines,
    talkHint,
    talkSend,
    talkClear
  };
  const visibleTabs = isNew ? TABS2.filter((item) => item.key !== "link") : TABS2;
  return /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
      PageHeader,
      {
        title: headline,
        subtitle: metaLine,
        eyebrow: "// SUBJECT STUDIO",
        onBack: close,
        right: /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(import_jsx_runtime19.Fragment, { children: [
          dirty ? /* @__PURE__ */ (0, import_jsx_runtime19.jsx)("span", { className: "cap", children: `\u6709 ${changed.length} \u5904\u6CA1\u4FDD\u5B58` }) : null,
          dirty ? /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(GlassButton, { size: "sm", onClick: revert, children: "\u653E\u5F03" }) : null,
          /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(PrimaryBtn, { onClick: save, disabled: !dirty, children: saveLabel })
        ] })
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)("div", { className: "screen screen--full", children: /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(
      "div",
      {
        style: {
          display: "grid",
          gridTemplateColumns: wide ? "352px minmax(0, 1fr)" : "minmax(0, 1fr)",
          gap: "var(--w-space-3)",
          alignItems: "start"
        },
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Stage, { ctx, headline, caption: previewCaption }),
          /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Pane, { ctx, tab, setTab, tabs: visibleTabs })
        ]
      }
    ) }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
      "div",
      {
        style: {
          position: "sticky",
          bottom: 0,
          zIndex: 5,
          borderTop: "1px solid var(--w-hairline)",
          padding: "var(--w-space-2) var(--w-pad-page)",
          background: "var(--w-surface-tint)",
          display: "flex",
          alignItems: "center",
          gap: "var(--w-space-2)",
          minHeight: 36
        },
        children: /* @__PURE__ */ (0, import_jsx_runtime19.jsx)("span", { style: { fontSize: "var(--w-size-small)", color: "var(--w-ink-body)", lineHeight: 1.5 }, children: status })
      }
    )
  ] });
}
function Stage({ ctx, headline, caption: caption7 }) {
  const { f, set, navigate, setFacing, facingLeft } = ctx;
  return /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Glass, { as: "section", tone: "card", radius: 12, style: { padding: "var(--w-pad-card)" }, children: /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 2, children: [
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
      "div",
      {
        style: {
          height: 300,
          borderRadius: 4,
          background: "var(--w-surface-tint)",
          display: "grid",
          placeItems: "center",
          overflow: "hidden"
        },
        children: /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "flex", flexDirection: "column", alignItems: "center", gap: "var(--w-space-3)", textAlign: "center" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(BodySm, { children: "\u8FD8\u6CA1\u6709\u7ACB\u7ED8" }),
          /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u5728\u4E0B\u9762\u300C\u7D20\u6750\u300D\u91CC\u7528\u4E00\u53E5\u8BDD\u751F\u6210\u4E00\u5957\uFF0C\u6216\u4ECE zip \u8865\u8FDB\u6765" })
        ] })
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "minmax(0,1fr) 64px", columnGap: "var(--w-space-3)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 1, children: [
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Hair, {}),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)("span", { style: { marginTop: "var(--w-space-1)" }, children: /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Eyebrow, { children: "// SUBJECT" }) }),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
          "span",
          {
            style: {
              fontSize: 27,
              fontWeight: 700,
              color: "var(--w-ink-display)",
              overflow: "hidden",
              textOverflow: "ellipsis",
              whiteSpace: "nowrap"
            },
            children: headline
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: caption7 })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
        "div",
        {
          style: {
            width: 64,
            height: 64,
            alignSelf: "end",
            borderRadius: 4,
            border: "1px solid var(--w-hairline)",
            background: "var(--w-surface-tint)",
            display: "grid",
            placeItems: "center",
            textAlign: "center"
          },
          children: /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "flex", flexDirection: "column" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime19.jsx)("span", { style: { fontSize: 20, color: "var(--w-ink-muted)" }, children: "\uFF0B" }),
            /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u5934\u50CF" })
          ] })
        }
      )
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 1px 1fr 1px 1fr", columnGap: "var(--w-space-2)", marginTop: "var(--w-space-2)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Reading2, { eyebrow: "AFFECTION", value: String(Math.round(f.affection)), note: "\u597D\u611F" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)("i", { style: { background: "var(--w-hairline)" } }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Reading2, { eyebrow: "TRUST", value: String(Math.round(f.trust)), note: "\u4FE1\u4EFB" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)("i", { style: { background: "var(--w-hairline)" } }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Reading2, { eyebrow: "ENERGY", value: String(Math.round(f.energy)), note: "\u7CBE\u529B" })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 2, style: { marginTop: "var(--w-space-2)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Hair, {}),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "34px minmax(0,1fr)", columnGap: "var(--w-space-1)", alignItems: "center" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u670D\u88C5" }),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Chips, { children: OUTFITS.map((item) => /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
          ChipBtn,
          {
            label: item.label,
            on: true,
            onClick: () => ctx.say(`\u5DF2\u6362\u4E0A\u300C${item.label}\u300D\u2014\u2014\u8FD8\u6CA1\u4FDD\u5B58\uFF0C\u6309\u300C\u4FDD\u5B58\u300D\u624D\u843D\u5230\u89D2\u8272\u8EAB\u4E0A\u3002`)
          },
          item.key
        )) })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "34px minmax(0,1fr)", columnGap: "var(--w-space-1)", alignItems: "center" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u8868\u60C5" }),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Chips, { children: EMOTIONS.map((item) => /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(ChipBtn, { label: item.label, on: true, onClick: () => void 0 }, item.key)) })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Chips, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(ChipBtn, { label: "\u2194 \u6362\u671D\u5411", onClick: () => setFacing(!facingLeft) }),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(ChipBtn, { label: "\u6362\u5934\u50CF", onClick: () => navigate("/avatarcrop") })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
        SwitchLine,
        {
          title: "\u6240\u6709\u5B58\u6863\u5171\u7528\u4E00\u4EFD\u8BB0\u5FC6",
          desc: "\u5173\u6389\u5C31\u4E00\u4EFD\u5B58\u6863\u8BB0\u4E00\u4EFD\uFF0C\u4E92\u4E0D\u4E32",
          checked: f.crossSave,
          onChange: (next) => set("crossSave", next, "\u8BB0\u5FC6\u4F5C\u7528\u57DF")
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)("div", { style: { display: "flex", justifyContent: "flex-end" }, children: /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { faint: true, children: "\u5360\u4F4D\u56FE\u8BB0 \xB7 \u9E23\u8C22 ignoredone.space \u4E2A\u4EBA\u56FE\u6807\u5305" }) })
    ] })
  ] }) });
}
function Reading2({ eyebrow: eyebrow2, value, note }) {
  return /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 1 }, children: [
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Eyebrow, { children: eyebrow2 }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "flex", alignItems: "flex-end", gap: "var(--w-space-1)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)("span", { style: { fontFamily: "var(--w-font-num)", fontSize: 24, color: "var(--w-ink-display)" }, children: value }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)("span", { style: { fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)", paddingBottom: 4 }, children: note })
    ] })
  ] });
}
function Pane({
  ctx,
  tab,
  setTab,
  tabs
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Glass, { as: "section", tone: "card", radius: 12, style: { padding: 0 }, children: [
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
      "div",
      {
        style: {
          display: "grid",
          gridTemplateColumns: `repeat(${tabs.length}, minmax(0, 1fr))`,
          gap: 6,
          padding: "var(--w-space-2) var(--w-pad-card)"
        },
        children: tabs.map((item) => /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
          "button",
          {
            type: "button",
            "aria-pressed": tab === item.key,
            onClick: () => setTab(item.key),
            style: {
              height: 30,
              borderRadius: 99,
              border: tab === item.key ? "1px solid transparent" : "1px solid var(--w-hairline)",
              background: tab === item.key ? "var(--w-ink-display)" : "var(--w-tint-button)",
              color: tab === item.key ? "var(--w-bg)" : "var(--w-ink-body)",
              font: "inherit",
              fontSize: "var(--w-size-caption)",
              cursor: "pointer",
              whiteSpace: "nowrap",
              padding: 0
            },
            children: item.label
          },
          item.key
        ))
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)("div", { style: { height: 2, background: "var(--w-signal)" } }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { padding: "var(--w-space-3) var(--w-pad-card) var(--w-pad-card)" }, children: [
      tab === "set" ? /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(SettingTab, { ctx }) : null,
      tab === "state" ? /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(StateTab, { ctx }) : null,
      tab === "art" ? /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(ArtTab, { ctx }) : null,
      tab === "mem" ? /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(MemoryTab, { ctx }) : null,
      tab === "link" ? /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(LinkTab, { ctx }) : null,
      tab === "talk" ? /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(TalkTab, { ctx }) : null,
      tab === "cg" ? /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(CgTab, {}) : null
    ] })
  ] });
}
function SettingTab({ ctx }) {
  const { f, set, say } = ctx;
  const aiRewrite = () => say("AI \u6CA1\u6709\u56DE\u5E94\uFF1A\u68C0\u67E5\u8BBE\u7F6E \u2192 AI \u91CC\u7684\u63A5\u53E3\u4E0E\u5BC6\u94A5\u3002");
  return /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 4, children: [
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(SectionHead, { no: "01", eyebrow: "// WHO", title: "TA \u662F\u8C01" }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 1fr", columnGap: "var(--w-space-3)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(FieldLine, { label: "\u59D3\u540D", value: f.name, onChange: (next) => set("name", next, "\u59D3\u540D") }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
        FieldLine,
        {
          label: "\u6635\u79F0\uFF08\u53EB TA \u4EC0\u4E48\uFF09",
          value: f.nickname,
          onChange: (next) => set("nickname", next, "\u6635\u79F0"),
          placeholder: "\u7559\u7A7A\u5C31\u7528\u59D3\u540D"
        }
      )
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 1fr", columnGap: "var(--w-space-3)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
        FieldLine,
        {
          label: "\u79F0\u547C TA \u5BF9\u4F60\u7684\u53EB\u6CD5",
          value: f.userAddress,
          onChange: (next) => set("userAddress", next, "\u79F0\u547C"),
          placeholder: "\u4E3B\u4EBA / \u54E5\u54E5 / \u540D\u5B57"
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-1)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(FieldLabel2, { children: "\u6027\u522B" }),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Chips, { children: GENDERS2.map((item) => /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(ChipBtn, { label: item, on: f.gender === item, onClick: () => set("gender", item, "\u6027\u522B") }, item)) }),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u60F3\u8981\u522B\u7684\u6027\u522B\uFF1F\u8BBE\u7F6E \u2192 \u6027\u522B\u5E93\u91CC\u52A0\u4E00\u4E2A\uFF0C\u8FD9\u91CC\u7ACB\u523B\u591A\u4E00\u9897\u3002" })
      ] })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 1fr", columnGap: "var(--w-space-3)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(FieldLine, { label: "\u5E74\u9F84", value: f.age, onChange: (next) => set("age", next, "\u5E74\u9F84"), placeholder: "\u4F8B\uFF1A24" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
        FieldLine,
        {
          label: "\u751F\u65E5",
          value: f.birthday,
          onChange: (next) => set("birthday", next, "\u751F\u65E5"),
          placeholder: "MM-dd\uFF0C\u5982 03-14"
        }
      )
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
      SwitchLine,
      {
        title: "\u81EA\u7136\u589E\u957F",
        desc: "\u5173\u6389\u540E\u5E74\u9F84\u5C31\u505C\u5728\u4F60\u586B\u7684\u8FD9\u4E2A\u6570\uFF1B\u5E74\u9F84\u4E0E\u751F\u65E5\u4ECD\u4F1A\u544A\u8BC9 AI\u3002",
        checked: f.naturalAging,
        onChange: (next) => set("naturalAging", next, "\u81EA\u7136\u589E\u957F")
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
      SwitchLine,
      {
        title: "\u604B\u7231\u5BF9\u8C61",
        desc: "\u5173\u6389\u540E TA \u4E0D\u4F1A\u4E3B\u52A8\u63A8\u8FDB\u5173\u7CFB\uFF0C\u5468\u671F\u4E0E\u4EB2\u5BC6\u5185\u5BB9\u4E5F\u4E0D\u8FDB\u63D0\u793A\u8BCD\u3002",
        checked: f.romance,
        onChange: (next) => set("romance", next, "\u604B\u7231\u5BF9\u8C61")
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
      FieldLine,
      {
        label: "\u6027\u683C",
        value: f.personality,
        onChange: (next) => set("personality", next, "\u6027\u683C"),
        placeholder: "TA \u7684\u8BF4\u8BDD\u65B9\u5F0F\u4E0E\u813E\u6C14",
        area: true,
        action: /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(GlassButton, { size: "sm", onClick: aiRewrite, children: "AI \u6539\u5199" })
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
      FieldLine,
      {
        label: "\u80CC\u666F / \u524D\u60C5",
        value: f.description,
        onChange: (next) => set("description", next, "\u80CC\u666F"),
        placeholder: "\u4F60\u4EEC\u4E4B\u95F4\u53D1\u751F\u8FC7\u4EC0\u4E48",
        area: true,
        action: /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(GlassButton, { size: "sm", onClick: aiRewrite, children: "AI \u6539\u5199" })
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
      FieldLine,
      {
        label: "\u5F00\u573A\u767D",
        value: f.greeting,
        onChange: (next) => set("greeting", next, "\u95EE\u5019\u8BED"),
        placeholder: "TA \u4E0A\u7EBF\u7B2C\u4E00\u53E5\u8BF4\u4EC0\u4E48",
        area: true,
        action: /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(GlassButton, { size: "sm", onClick: aiRewrite, children: "AI \u6539\u5199" })
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Hair, {}),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 3, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(PaneTitle, { children: "\u9AD8\u7EA7\u8BBE\u7F6E \xB7 \u9009\u586B" }),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(GlassButton, { size: "sm", onClick: ctx.toggleAdvanced, children: ctx.advancedOpen ? "\u6536\u8D77 \u25B2" : "\u5C55\u5F00 \u25BC" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u4E0D\u586B\u4E5F\u6CA1\u5173\u7CFB\uFF1A\u8FD9\u4E9B\u680F\u7559\u7A7A\u65F6\u5B8C\u5168\u4E0D\u4F1A\u5199\u8FDB\u63D0\u793A\u8BCD\uFF0CTA \u4ECD\u6309\u6027\u683C\u81EA\u7136\u8BF4\u8BDD\u3002" }),
      ctx.advancedOpen ? /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 3, children: [
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
          FieldLine,
          {
            label: "\u9053\u5FB7\u89C2",
            value: f.moral,
            onChange: (next) => set("moral", next, "\u9053\u5FB7\u89C2"),
            placeholder: "TA \u89C9\u5F97\u4EC0\u4E48\u8BE5\u505A\u3001\u4EC0\u4E48\u7EDD\u4E0D\u8D8A\u7EBF\uFF08\u53EF\u7559\u7A7A\uFF09",
            area: true
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
          FieldLine,
          {
            label: "\u4EBA\u751F\u89C2",
            value: f.life,
            onChange: (next) => set("life", next, "\u4EBA\u751F\u89C2"),
            placeholder: "TA \u89C9\u5F97\u4EC0\u4E48\u503C\u5F97\u3001\u600E\u4E48\u770B\u805A\u6563\u4E0E\u65E5\u5B50\uFF08\u53EF\u7559\u7A7A\uFF09",
            area: true
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
          FieldLine,
          {
            label: "\u751F\u5E73",
            value: f.biography,
            onChange: (next) => set("biography", next, "\u751F\u5E73"),
            placeholder: "TA \u4ECE\u54EA\u6765\u3001\u7ECF\u5386\u8FC7\u4EC0\u4E48\uFF08\u53EF\u7559\u7A7A\uFF09",
            area: true
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
          FieldLine,
          {
            label: "\u653F\u6CBB\u503E\u5411",
            value: f.political,
            onChange: (next) => set("political", next, "\u653F\u6CBB\u503E\u5411"),
            placeholder: "TA \u5BF9\u6743\u529B\u3001\u79E9\u5E8F\u4E0E\u7ACB\u573A\u7684\u770B\u6CD5\uFF08\u53EF\u7559\u7A7A\uFF09",
            area: true
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
          FieldLine,
          {
            label: "\u601D\u7EF4\u4E3B\u4E49 / \u610F\u8BC6\u5F62\u6001",
            value: f.ideology,
            onChange: (next) => set("ideology", next, "\u601D\u7EF4\u4E3B\u4E49"),
            placeholder: "TA \u7528\u4EC0\u4E48\u773C\u5149\u770B\u4E16\u754C\u3001\u4FE1\u4EC0\u4E48\u9053\u7406\uFF08\u53EF\u7559\u7A7A\uFF09",
            area: true
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
          FieldLine,
          {
            label: "\u6DF1\u523B\u8BB0\u5FC6\uFF08\u53EF\u5206\u884C\u5199\uFF09",
            value: f.deepMemories,
            onChange: (next) => set("deepMemories", next, "\u6DF1\u523B\u8BB0\u5FC6"),
            placeholder: "\u4E00\u884C\u4E00\u6761\uFF0CTA \u5FD8\u4E0D\u6389\u7684\u90A3\u51E0\u4EF6\u4E8B\uFF08\u53EF\u7559\u7A7A\uFF09",
            area: true
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
          FieldLine,
          {
            label: "\u521B\u4F24\u80CC\u666F",
            value: f.trauma,
            onChange: (next) => set("trauma", next, "\u521B\u4F24\u80CC\u666F"),
            placeholder: "TA \u53D7\u8FC7\u7684\u4F24\u3001\u7559\u4E0B\u7684\u9634\u5F71\uFF08\u53EF\u7559\u7A7A\uFF09",
            area: true
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u7CBE\u795E\u72B6\u51B5\u8BF7\u5728\u8D85\u7EA7\u76D1\u6D4B / \u5FC3\u7406\u5065\u5EB7\u5904\u914D\u7F6E\uFF0C\u4E0D\u5728\u8FD9\u91CC\u65B0\u589E\u5B57\u6BB5\u3002" })
      ] }) : null
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Hair, {}),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 3, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(PaneTitle, { children: "\u8BA9 AI \u5199\u4E00\u6574\u4EFD\u8BBE\u5B9A" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u4E00\u53E5\u8BDD\u63CF\u8FF0\u60F3\u8981\u7684\u4EBA\uFF0CTA \u7684\u540D\u5B57\u3001\u6027\u522B\u3001\u6027\u683C\u3001\u80CC\u666F\u3001\u5F00\u573A\u767D\u4E00\u6B21\u586B\u597D\u3002\u5199\u8FDB\u5B57\u6BB5\u540E\u4ECD\u8981\u6309\u300C\u4FDD\u5B58\u300D\u624D\u843D\u5230\u76D8\u4E0A\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(AiBrief, { onGenerate: say })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Hair, {}),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 3, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(PaneTitle, { children: "TA \u7684\u4E16\u754C\u89C2" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u53EF\u591A\u672C\u590D\u5408\uFF0C\u4F5C\u4E3A\u751F\u6210 TA \u4E16\u754C\u89C2\u7684\u8BED\u6599\u3002\u89D2\u8272\u4E16\u754C\u89C2\u53EF\u4EE5\u4E0E\u5B58\u6863\u4E16\u754C\u4E66\u4E0D\u540C\uFF1B\u5B58\u6863\u5185\u4ECD\u53EF\u6539\u4E16\u754C\u89C2\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 1, children: [
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(FieldLabel2, { children: "\u4E16\u754C\u4E66\uFF08\u53EF\u591A\u9009\uFF09" }),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u8FD8\u6CA1\u9009\u4E16\u754C\u4E66\uFF1ATA \u7684\u4E16\u754C\u89C2\u53EA\u9760\u8BBE\u5B9A\u6587\u5B57\u3002\u53EF\u591A\u672C\u590D\u5408\uFF0C\u4F5C\u4E3A\u751F\u6210 TA \u4E16\u754C\u89C2\u7684\u8BED\u6599\u3002" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Hair, {}),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 1 }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(BodySm, { children: "\u89D2\u8272\u4E16\u754C\u89C2" }),
          /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u73B0\u5728\u542F\u7528\uFF1A\u6CA1\u6709\u89D2\u8272\u4E16\u754C\u89C2" })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(GlassButton, { size: "sm", children: "\u65B0\u5EFA\u4E00\u5957" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u4E00\u5957 = \u51E0\u672C\u4E16\u754C\u4E66 + \u4E00\u6BB5\u63CF\u8FF0\u3002\u70B9\u4E00\u884C\u5C31\u662F\u6307\u5B9A TA \u9ED8\u8BA4\u7528\u54EA\u4E00\u5957\uFF08\u5355\u9009\uFF09\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u8FD8\u6CA1\u6709\u89D2\u8272\u4E16\u754C\u89C2\uFF1ATA \u5C31\u7528\u4E0A\u9762\u52FE\u7684\u4E16\u754C\u4E66\uFF0C\u6216\u8005\u6CBF\u7528\u8FD9\u4E00\u6863\u7684\u4E16\u754C\u6811\u3002" })
    ] })
  ] });
}
function AiBrief({ onGenerate }) {
  const [brief, setBrief] = (0, import_react14.useState)("");
  return /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(import_jsx_runtime19.Fragment, { children: /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "minmax(0,1fr) auto", columnGap: "var(--w-space-3)" }, children: [
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
      Field,
      {
        value: brief,
        onChange: setBrief,
        placeholder: "\u4F8B\uFF1A\u9752\u6885\u7AF9\u9A6C\u7684\u90BB\u5C45\u59D0\u59D0\uFF0C\u5B66\u533B\uFF0C\u5634\u4E0A\u5ACC\u5F03\u5176\u5B9E\u5F88\u60E6\u8BB0\u4EBA"
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
      PrimaryBtn,
      {
        onClick: () => onGenerate(
          brief.trim() ? "AI \u6CA1\u6709\u56DE\u5E94\uFF1A\u68C0\u67E5\u8BBE\u7F6E \u2192 AI \u91CC\u7684\u63A5\u53E3\u4E0E\u5BC6\u94A5\u3002" : "\u5148\u7528\u4E00\u53E5\u8BDD\u8BF4\u60F3\u8981\u4EC0\u4E48\u6837\u7684\u89D2\u8272\uFF08\u4F8B\u5982\uFF1A\u9752\u6885\u7AF9\u9A6C\u7684\u90BB\u5C45\u59D0\u59D0\uFF0C\u5B66\u533B\uFF0C\u5634\u4E0A\u5ACC\u5F03\u5176\u5B9E\u5F88\u60E6\u8BB0\u4EBA\uFF09\u3002"
        ),
        children: "\u751F\u6210"
      }
    )
  ] }) });
}
function FieldLabel2({ children }) {
  return /* @__PURE__ */ (0, import_jsx_runtime19.jsx)("span", { style: { fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)" }, children });
}
function FieldLine({
  label,
  value,
  onChange,
  placeholder,
  action,
  area
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-1)" }, children: [
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "flex", alignItems: "flex-end", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(FieldLabel2, { children: label }),
      action
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Field, { value, onChange, placeholder, multiline: area })
  ] });
}
function StateTab({ ctx }) {
  const { f, set, navigate, resetSubProfile } = ctx;
  const cycleNote = f.cycleTracked ? `\u7B2C ${f.cycleDay || 0} \u5929 \xB7 \u9636\u6BB5 ${f.cyclePhase}` : "\u6CA1\u6709\u5F00\u5468\u671F\u8BB0\u5F55\uFF1ATA \u7684\u63D0\u793A\u8BCD\u91CC\u4E0D\u4F1A\u51FA\u73B0\u8EAB\u4F53\u5468\u671F\u3002";
  return /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 4, children: [
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(SectionHead, { no: "02", eyebrow: "// STATUS", title: "TA \u6B64\u523B\u7684\u72B6\u6001" }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u62D6\u52A8\u5C31\u6539\uFF0C\u4F46\u548C\u8BBE\u5B9A\u4E00\u6837\uFF1A\u6309\u300C\u4FDD\u5B58\u300D\u624D\u5199\u8FDB\u89D2\u8272\u3002\u8FD9\u51E0\u9879\u76F4\u63A5\u8FDB AI \u7684\u63D0\u793A\u8BCD\u3002" }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
      SliderLine,
      {
        label: "\u597D\u611F",
        reading: `${Math.round(f.affection)} / 100`,
        value: f.affection,
        onChange: (next) => set("affection", next, "\u597D\u611F")
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
      SliderLine,
      {
        label: "\u4FE1\u4EFB",
        reading: `${Math.round(f.trust)} / 100`,
        value: f.trust,
        onChange: (next) => set("trust", next, "\u4FE1\u4EFB")
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 1, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
        SliderLine,
        {
          label: "\u7CBE\u529B",
          reading: `${Math.round(f.energy)} / 100`,
          value: f.energy,
          onChange: (next) => set("energy", next, "\u7CBE\u529B")
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u7CBE\u529B\u4F4E\u7684\u65F6\u5019 TA \u4F1A\u60F3\u7761\u3001\u8BF4\u8BDD\u4E5F\u77ED\u3002" })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u6027\u6B32\u7CFB\u7EDF\u5728\u8BBE\u7F6E \u2192 \u89D2\u8272\u4E0E\u5185\u5BB9\u91CC\u662F\u5173\u7740\u7684\uFF1A\u8FD9\u91CC\u7684\u6570\u5B57\u53EA\u7559\u5728\u6863\u6848\u4E0A\uFF0C\u6E38\u620F\u91CC\u6682\u65F6\u4E0D\u4F1A\u53C2\u4E0E\u3002\u6253\u5F00\u603B\u5F00\u5173\u540E\u624D\u4F1A\u751F\u6548\u3002" }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
      SliderLine,
      {
        label: "\u6027\u6B32",
        reading: `${Math.round(f.libido)} / 100`,
        value: f.libido,
        onChange: (next) => set("libido", next, "\u6027\u6B32")
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
      SwitchLine,
      {
        title: "\u8BBE\u5B9A\u9501",
        desc: "\u9501\u4E0A\u4E4B\u540E\u6027\u6B32\u505C\u5728\u4F60\u5199\u7684\u503C\u4E0A\uFF1A\u5468\u671F\u4E0E AI \u5224\u5B9A\u90FD\u6539\u4E0D\u52A8\u5B83\u3002",
        checked: f.libidoLocked,
        onChange: (next) => set("libidoLocked", next, "\u6027\u6B32\u8BBE\u5B9A\u9501")
      }
    ),
    f.libidoLocked ? /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
      SliderLine,
      {
        label: "\u9501\u5728",
        reading: String(Math.round(f.libidoLockValue)),
        value: f.libidoLockValue,
        onChange: (next) => set("libidoLockValue", next, "\u6027\u6B32\u8BBE\u5B9A\u9501")
      }
    ) : null,
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
      FieldLine,
      {
        label: "\u597D\u611F\u79EF\u5206\uFF08\u5347\u7EA7\u7528\u7684\u90A3\u628A\u6570\uFF09",
        value: f.affectionPoints,
        onChange: (next) => set("affectionPoints", next, "\u597D\u611F\u79EF\u5206")
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(FieldLine, { label: "\u6B64\u523B\u5728\u54EA", value: f.location, onChange: (next) => set("location", next, "\u5730\u70B9"), placeholder: "\u5BB6 / \u5B66\u6821 / \u533B\u9662" }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-1)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(FieldLabel2, { children: "\u5FC3\u60C5" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Chips, { children: MOODS.map((item) => /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(ChipBtn, { label: item.label, on: f.mood === item.key, onClick: () => set("mood", item.key, "\u5FC3\u60C5") }, item.key)) })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Hair, {}),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 3, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
        SwitchLine,
        {
          title: "\u8BB0\u8EAB\u4F53\u5468\u671F",
          desc: "\u5173\u6389\u540E\u63D0\u793A\u8BCD\u91CC\u4E0D\u4F1A\u51FA\u73B0\u5468\u671F\uFF0C\u4E0E TA \u7684\u6027\u522B\u65E0\u5173\u3002",
          checked: f.cycleTracked,
          onChange: (next) => set("cycleTracked", next, "\u5468\u671F")
        }
      ),
      f.cycleTracked ? /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 1fr", columnGap: "var(--w-space-3)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(FieldLine, { label: "\u7B2C\u51E0\u5929", value: f.cycleDay, onChange: (next) => set("cycleDay", next, "\u5468\u671F") }),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
          FieldLine,
          {
            label: "\u9636\u6BB5",
            value: f.cyclePhase,
            onChange: (next) => set("cyclePhase", next, "\u5468\u671F"),
            placeholder: "menstrual / follicular / luteal"
          }
        )
      ] }) : null,
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: cycleNote })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Hair, {}),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 3, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(PaneTitle, { children: "\u526F\u8BBE\u5B9A\uFF08\u5F53\u4E0B\u8FD9\u4E00\u9635\u7684 TA\uFF09" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u4E3B\u8BBE\u5B9A\u662F\u300CTA \u662F\u4EC0\u4E48\u6837\u7684\u4EBA\u300D\uFF0C\u526F\u8BBE\u5B9A\u662F\u300CTA \u6700\u8FD1\u600E\u4E48\u6837\u300D\u3002\u5B83\u4F1A\u81EA\u5DF1\u6F02\uFF0C\u4E5F\u80FD\u56DE\u6B63\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Well2, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(BodySm, { children: "\u5BF9\u4F60\u8FD8\u6709\u4E9B\u751F\u758F\uFF0C\u8BF4\u8BDD\u4F1A\u7559\u4E00\u70B9\u8DDD\u79BB\uFF1B\u5FC3\u91CC\u5BF9\u4F60\u8FD8\u6709\u4FDD\u7559\u3002" }),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\uFF08\u8FD8\u6CA1\u6709\u8BB0\u4E0B\u7684\u5FC3\u4E8B\uFF09" }),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u4E3B\u8BBE\u5B9A\uFF08\u5185\u6838\uFF0C\u4E0D\u968F\u65F6\u95F4\u6539\u53D8\uFF09" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Actions, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(GlassButton, { size: "sm", onClick: () => navigate("/subProfile"), children: "\u770B\u5B8C\u6574\u526F\u8BBE\u5B9A" }),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(GlassButton, { size: "sm", onClick: resetSubProfile, children: "\u56DE\u6B63\u5230\u4E3B\u8BBE\u5B9A" }),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(GlassButton, { size: "sm", onClick: () => navigate("/mentalHealth"), children: "\u5FC3\u7406\u72B6\u51B5" })
      ] })
    ] })
  ] });
}
function ArtTab({ ctx }) {
  const { navigate } = ctx;
  return /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 4, children: [
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(SectionHead, { no: "03", eyebrow: "// PORTRAIT", title: "\u670D\u88C5 \xD7 \u8868\u60C5" }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u4E00\u683C\u5C31\u662F\u4E00\u5F20\u7ACB\u7ED8\u3002\u70B9\u6709\u56FE\u7684\u683C\u5B50\u6362\u6210\u90A3\u5F20\uFF0C\u70B9\u7A7A\u7684\u683C\u5B50\u76F4\u63A5\u53BB\u751F\u6210\u5B83\u3002" }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 1, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(BodySm, { children: OUTFITS[0].label }),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "0/1 \u683C\u6709\u56FE" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Chips, { children: EMOTIONS.map((item) => /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
        "span",
        {
          style: {
            width: 76,
            height: 86,
            borderRadius: 4,
            border: "1px solid var(--w-hairline)",
            background: "var(--w-tint-card)",
            display: "flex",
            alignItems: "flex-start",
            justifyContent: "flex-end",
            padding: 4
          },
          children: /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u7F3A" })
        },
        item.key
      )) })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Hair, {}),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 3, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(PaneTitle, { children: "\u751F\u6210\u8FD9\u4E00\u683C" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(ArtPrompt, {}),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u753B\u9762\u751F\u6210\u6CA1\u5F00\u542F\uFF1A\u8BBE\u7F6E \u2192 AI \u2192 \u753B\u9762\u751F\u6210\u91CC\u586B\u597D\u63A5\u53E3\u4E0E\u5BC6\u94A5\uFF0C\u8FD9\u4E00\u683C\u624D\u80FD\u751F\u6210\u3002" })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Actions, { children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(GlassButton, { size: "sm", children: "\u5BFC\u5165\u7D20\u6750\u5305\uFF08zip\uFF09" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(GlassButton, { size: "sm", onClick: () => navigate("/figurePack"), children: "\u5F62\u8C61\u5305\u2026" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(GlassButton, { size: "sm", onClick: () => navigate("/avatarcrop"), children: "\u6362\u5934\u50CF" })
    ] })
  ] });
}
function ArtPrompt() {
  const [prompt, setPrompt] = (0, import_react14.useState)("");
  return /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-3)" }, children: [
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
      Field,
      {
        value: prompt,
        onChange: setPrompt,
        placeholder: "\u4F8B\uFF1A\u767D\u8272\u9488\u7EC7\u886B\u914D\u6D45\u8272\u957F\u88D9\uFF0C\u7AD9\u5728\u5348\u540E\u7A97\u8FB9\uFF0C\u5FAE\u5FAE\u7B11",
        multiline: true
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-3)" }, children: /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(PrimaryBtn, { disabled: true, children: "\u753B\u7ACB\u7ED8" }) })
  ] });
}
function MemoryTab({ ctx }) {
  const { f, set, navigate } = ctx;
  const scopeLabel = f.crossSave ? "\u6240\u6709\u5B58\u6863\u5171\u7528\u8FD9\u4E00\u4EFD\u8BB0\u5FC6" : "\u53EA\u5728\u8FD9\u4EFD\u5B58\u6863\u91CC";
  const scopePath = f.crossSave ? "memories.json" : "memories";
  const memoryHint = f.crossSave ? "\u73B0\u5728\u6240\u6709\u5B58\u6863\u5171\u7528\u540C\u4E00\u4EFD\u8BB0\u5FC6\uFF1A\u5728\u8FD9\u4EFD\u5B58\u6863\u91CC\u804A\u8FC7\u7684\u4E8B\uFF0C\u6362\u4E2A\u5B58\u6863 TA \u4E5F\u8BB0\u5F97\u3002\u60F3\u8981\u300C\u6362\u4E86\u5B58\u6863\u5C31\u91CD\u65B0\u8BA4\u8BC6\u300D\uFF0C\u628A\u4E0B\u9762\u90A3\u9897\u5F00\u5173\u5173\u6389\u3002" : "\u73B0\u5728\u8BB0\u5FC6\u6309\u5B58\u6863\u9694\u79BB\uFF1A\u8FD9\u4E00\u4EFD\u5B58\u6863\u91CC\u53D1\u751F\u7684\u4E8B\u53EA\u5C5E\u4E8E\u8FD9\u4E00\u4EFD\u3002\u60F3\u8BA9\u5B83\u8DDF\u7740\u89D2\u8272\u8DE8\u5B58\u6863\u8D70\uFF0C\u6253\u5F00\u4E0B\u9762\u90A3\u9897\u5F00\u5173\uFF08\u5BF9\u6240\u6709\u4EBA\u90FD\u751F\u6548\uFF09\u3002";
  const engineText = f.voiceEngine === "" ? "\u5F15\u64CE\u8DDF\u968F\u5168\u5C40" : engineLabel(f.voiceEngine);
  const nameText = f.voiceName.trim() ? `\u97F3\u8272 ${f.voiceName.trim()}` : "\u97F3\u8272\u8DDF\u968F\u5168\u5C40";
  const extraText = f.voiceExtra.trim() ? ` \xB7 \u53C2\u6570 ${f.voiceExtra.trim()}` : "";
  const hasApi = f.voiceApiUrl.trim().length > 0 && f.voiceApiKey.trim().length > 0;
  const halfApi = f.voiceApiUrl.trim().length > 0 !== f.voiceApiKey.trim().length > 0;
  const apiText = hasApi ? ` \xB7 \u4E13\u5C5EAPI ${f.voiceApiMode === "" ? "\u534F\u8BAE\u8DDF\u968F\u5168\u5C40" : apiModeLabel(f.voiceApiMode)} \xB7 ${f.voiceTtsModel.trim() ? `\u6A21\u578B ${f.voiceTtsModel.trim()}` : "\u6A21\u578B\u8DDF\u968F\u5168\u5C40"}` : halfApi ? " \xB7 \u26A0 \u4E13\u5C5E API \u53EA\u586B\u4E86\u5730\u5740\u6216\u5BC6\u94A5\u7684\u4E00\u534A\uFF0C\u5DF2\u6574\u4F53\u56DE\u9000\u5168\u5C40" : " \xB7 API \u8DDF\u968F\u5168\u5C40";
  const voiceSummary = f.voiceEnabled ? engineText + " \xB7 " + nameText + extraText + apiText : "\u8FD9\u4E2A\u89D2\u8272\u4E0D\u51FA\u58F0\uFF1A\u6717\u8BFB\u4E0E\u914D\u97F3\u90FD\u4F1A\u8DF3\u8FC7 TA\u3002";
  return /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 4, children: [
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(SectionHead, { no: "04", eyebrow: "// MEMORY", title: "TA \u8BB0\u5F97\u4EC0\u4E48" }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Stack, { gap: 0, children: LAYERS.map((layer) => /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(
      "div",
      {
        style: {
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          gap: "var(--w-space-3)",
          padding: "5px 0"
        },
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 1, minWidth: 0 }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(BodySm, { children: layer.name }),
            /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: layer.detail })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u2014" })
        ]
      },
      layer.name
    )) }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Hair, {}),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 3, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(PaneTitle, { children: "\u8FD9\u4EFD\u8BB0\u5FC6\u5F52\u8C01" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
        SwitchLine,
        {
          title: "\u6240\u6709\u5B58\u6863\u5171\u7528\u4E00\u4EFD\u8BB0\u5FC6",
          desc: scopePath,
          checked: f.crossSave,
          onChange: (next) => set("crossSave", next, "\u8BB0\u5FC6\u4F5C\u7528\u57DF")
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(BodySm, { children: scopeLabel }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: memoryHint }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u8FD9\u9897\u5F00\u5173\u662F\u5168\u5E94\u7528\u7684\uFF1A\u6539\u4E86\u5B83\uFF0C\u6240\u6709\u89D2\u8272\u3001\u6240\u6709\u5B58\u6863\u4E00\u8D77\u6362\u6863\u3002" })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Hair, {}),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 3, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(PaneTitle, { children: "\u8FD9\u4E00\u4E2A\u89D2\u8272\u7684 AI \u53C2\u6570" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u7559\u7A7A = \u8DDF\u968F\u5168\u5C40\u8BBE\u7F6E\u3002\u586B\u4E86\u5C31\u53EA\u6539 TA\uFF0C\u522B\u4EBA\u7684\u4E0D\u52A8\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "repeat(3, minmax(0,1fr))", columnGap: "var(--w-space-3)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(FieldLine, { label: "\u8BB0\u5FC6\u6761\u6570", value: f.ovMemoryTurns, onChange: (next) => set("ovMemoryTurns", next, "\u8BB0\u5FC6\u53C2\u6570"), placeholder: "\u5168\u5C40" }),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(FieldLine, { label: "\u4E0A\u4E0B\u6587\u4E0A\u9650", value: f.ovContextLimit, onChange: (next) => set("ovContextLimit", next, "\u8BB0\u5FC6\u53C2\u6570"), placeholder: "\u5168\u5C40" }),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(FieldLine, { label: "\u6E29\u5EA6", value: f.ovTemperature, onChange: (next) => set("ovTemperature", next, "\u8BB0\u5FC6\u53C2\u6570"), placeholder: "\u5168\u5C40" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 1fr", columnGap: "var(--w-space-3)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(FieldLine, { label: "\u9057\u5FD8\u6307\u6570\uFF08\u5929\uFF09", value: f.ovWindowDays, onChange: (next) => set("ovWindowDays", next, "\u8BB0\u5FC6\u53C2\u6570"), placeholder: "\u5168\u5C40" }),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(FieldLine, { label: "\u56DE\u5FC6\u968F\u673A\u5EA6", value: f.ovRecall, onChange: (next) => set("ovRecall", next, "\u8BB0\u5FC6\u53C2\u6570"), placeholder: "\u5168\u5C40" })
      ] })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Hair, {}),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 3, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(PaneTitle, { children: "\u8FD9\u4E00\u4E2A\u89D2\u8272\u7684\u58F0\u97F3" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
        SwitchLine,
        {
          title: "\u8BA9 TA \u51FA\u58F0",
          desc: "\u5173\u6389\u540E TA \u4E0D\u518D\u5F00\u53E3\u6717\u8BFB\uFF1A\u5BF9\u8BDD\u7167\u5E38\uFF0C\u53EA\u662F\u6CA1\u6709\u58F0\u97F3\uFF0C\u522B\u7684\u89D2\u8272\u4E0D\u53D7\u5F71\u54CD\u3002",
          checked: f.voiceEnabled,
          onChange: (next) => set("voiceEnabled", next, "\u8BED\u97F3")
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: voiceSummary }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(FieldLabel2, { children: "\u8BED\u97F3\u5F15\u64CE" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Chips, { children: VOICE_ENGINES.map((item) => /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
        ChipBtn,
        {
          label: item.label,
          on: f.voiceEngine === item.key,
          onClick: () => set("voiceEngine", item.key, "\u8BED\u97F3\u5F15\u64CE")
        },
        item.key || "global"
      )) }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 1fr", columnGap: "var(--w-space-3)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(FieldLine, { label: "\u97F3\u8272", value: f.voiceName, onChange: (next) => set("voiceName", next, "\u97F3\u8272"), placeholder: "\u7559\u7A7A\u8DDF\u968F\u5168\u5C40" }),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(FieldLine, { label: "\u989D\u5916\u53C2\u6570", value: f.voiceExtra, onChange: (next) => set("voiceExtra", next, "\u8BED\u97F3\u53C2\u6570"), placeholder: "\u7559\u7A7A\u8DDF\u968F\u5168\u5C40" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(FieldLabel2, { children: "\u4E13\u5C5E\u8BED\u97F3 API\uFF08\u53EF\u9009\uFF0C\u5730\u5740+\u5BC6\u94A5\u90FD\u586B\u624D\u751F\u6548\uFF09" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 1fr", columnGap: "var(--w-space-3)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(FieldLine, { label: "API \u5730\u5740", value: f.voiceApiUrl, onChange: (next) => set("voiceApiUrl", next, "\u8BED\u97F3 API"), placeholder: "\u7559\u7A7A\u8DDF\u968F\u5168\u5C40" }),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
          FieldLine,
          {
            label: "API \u5BC6\u94A5",
            value: f.voiceApiKey,
            onChange: (next) => set("voiceApiKey", next, "\u8BED\u97F3 API"),
            placeholder: "\u7559\u7A7A\u8DDF\u968F\u5168\u5C40"
          }
        )
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(FieldLabel2, { children: "API \u534F\u8BAE" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Chips, { children: VOICE_API_MODES.map((item) => /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
        ChipBtn,
        {
          label: item.label,
          on: f.voiceApiMode === item.key,
          onClick: () => set("voiceApiMode", item.key, "\u8BED\u97F3 API")
        },
        item.key || "global"
      )) }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
        FieldLine,
        {
          label: "TTS \u6A21\u578B",
          value: f.voiceTtsModel,
          onChange: (next) => set("voiceTtsModel", next, "\u8BED\u97F3 API"),
          placeholder: "\u7559\u7A7A\u8DDF\u968F\u5168\u5C40\uFF08\u5982 tts-1 / tts-1-hd\uFF09"
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)("div", { children: /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(GlassButton, { size: "sm", onClick: () => ctx.say("\u8FD9\u4E2A\u89D2\u8272\u7684\u8BED\u97F3\u8BBE\u7F6E\u5DF2\u4FDD\u5B58\u3002"), children: "\u4FDD\u5B58\u8FD9\u4E00\u6863\u8BED\u97F3" }) }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Hair, {}),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(BodySm, { children: "\u53EA\u7ED9 TA \u7528\u7684\u6A21\u578B" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u8BBE\u7F6E \u2192 AI \u91CC\u8FD8\u6CA1\u6709\u53EF\u7528\u7684\u5382\u5546\uFF1A\u586B\u597D\u5730\u5740\u4E0E\u5BC6\u94A5\u5E76\u6253\u5F00\uFF0C\u8FD9\u91CC\u624D\u6709\u5F97\u9009\u3002\u73B0\u5728 TA \u8D70\u5168\u5C40\u8BBE\u7F6E\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(FieldLabel2, { children: "\u5382\u5546" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Chips, { children: /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(ChipBtn, { label: "\u8DDF\u968F\u5168\u5C40", on: true }) }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(FieldLabel2, { children: "\u6A21\u578B" })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Hair, {}),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 3, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(PaneTitle, { children: "\u628A\u522B\u5904\u7684\u8BB0\u5FC6\u642C\u8FDB\u6765" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u7075\u67A2\uFF08Lingshu\uFF09\u5BFC\u51FA\u7684\u8BB0\u5FC6 JSON \u53EF\u4EE5\u76F4\u63A5\u8BFB\u8FDB\u6765\uFF1A\u8FFD\u52A0\u662F\u5E76\u8FDB\u6765\uFF0C\u66FF\u6362\u662F\u5148\u6E05\u6389 TA \u73B0\u5728\u8BB0\u5F97\u7684\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Actions, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(GlassButton, { size: "sm", children: "\u8FFD\u52A0\u8BB0\u5FC6" }),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(GlassButton, { size: "sm", children: "\u66FF\u6362\u8BB0\u5FC6" })
      ] })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Actions, { children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(GlassButton, { size: "sm", onClick: () => navigate("/memoryManage"), children: "\u6838\u5FC3\u8BB0\u5FC6" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(GlassButton, { size: "sm", onClick: () => navigate("/memoryGraph"), children: "\u5173\u7CFB\u7F51" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(GlassButton, { size: "sm", onClick: () => navigate("/memorySearch"), children: "\u641C\u4E8B\u4EF6" })
    ] })
  ] });
}
function engineLabel(key) {
  return VOICE_ENGINES.find((item) => item.key === key)?.label ?? key;
}
function apiModeLabel(key) {
  return VOICE_API_MODES.find((item) => item.key === key)?.label ?? key;
}
function LinkTab({ ctx }) {
  const { f, set } = ctx;
  const allowedHint = f.allowed ? "TA \u5728\u8FD9\u4EFD\u5B58\u6863\u7684\u53EF\u7528\u914D\u89D2\u91CC\uFF0C\u804A\u5929\u65F6 AI \u80FD\u627E\u5230 TA\u3002\u5F53\u524D\u540D\u5355\u662F\u7A7A\u7684 = \u4E0D\u9650\u5236\uFF0C\u89D2\u8272\u5E93\u91CC\u6240\u6709\u4EBA\u90FD\u80FD\u88AB AI \u8C03\u7528\u3002" : "TA \u5DF2\u88AB\u79FB\u51FA\u8FD9\u4EFD\u5B58\u6863\uFF1AAI \u4E0D\u4F1A\u518D\u968F\u624B\u628A TA \u62C9\u8FDB\u5BF9\u8BDD\u3002\u5F53\u524D\u540D\u5355\u662F\u7A7A\u7684 = \u4E0D\u9650\u5236\uFF0C\u89D2\u8272\u5E93\u91CC\u6240\u6709\u4EBA\u90FD\u80FD\u88AB AI \u8C03\u7528\u3002";
  return /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 4, children: [
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(SectionHead, { no: "05", eyebrow: "// THIS SAVE", title: "TA \u5728\u8FD9\u4EFD\u5B58\u6863\u91CC" }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
      SwitchLine,
      {
        title: "\u8FD9\u4EFD\u5B58\u6863\u7684\u53EF\u7528\u914D\u89D2",
        desc: allowedHint,
        checked: f.allowed,
        onChange: (next) => {
          set("allowed", next, "\u53EF\u7528\u914D\u89D2");
          ctx.say(
            next ? `\u5DF2\u628A\u300C${f.name.trim()}\u300D\u52A0\u8FDB\u672C\u5B58\u6863\u7684\u53EF\u7528\u914D\u89D2\u3002` : `\u5DF2\u628A\u300C${f.name.trim()}\u300D\u79FB\u51FA\u672C\u5B58\u6863\u7684\u53EF\u7528\u914D\u89D2\u3002`
          );
        }
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Hair, {}),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 3, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(PaneTitle, { children: "\u590D\u5236\u4E00\u4E2A TA" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u526F\u672C\u5171\u7528\u540C\u4E00\u6279\u7ACB\u7ED8\u6587\u4EF6\uFF0C\u4E0D\u5360\u53CC\u4EFD\u78C1\u76D8\u3002\u8BB0\u5FC6\u53EF\u4EE5\u4E00\u8D77\u5E26\u8D70\uFF0C\u4E5F\u53EF\u4EE5\u4ECE\u96F6\u5F00\u59CB\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(FieldLine, { label: "\u526F\u672C\u540D\u5B57", value: f.duplicateName, onChange: (next) => set("duplicateName", next, "\u526F\u672C\u540D\u5B57") }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Actions, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(GlassButton, { size: "sm", children: "\u53EA\u8981\u8D44\u6599" }),
        /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(PrimaryBtn, { children: "\u8FDE\u8BB0\u5FC6\u4E00\u8D77\u590D\u5236" })
      ] })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Well2, { children: /* @__PURE__ */ (0, import_jsx_runtime19.jsx)("div", { style: { textAlign: "center" }, children: /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u6682\u65E0\u5B58\u6863" }) }) })
  ] });
}
function CgTab() {
  return /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 4, children: [
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(SectionHead, { no: "07", eyebrow: "// GALLERY", title: "TA \u7684\u753B\u9762\u6536\u85CF" }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u5267\u60C5\u91CC\u89E6\u53D1\u8FC7\u624D\u7B97\u89E3\u9501\uFF1B\u89E3\u9501\u524D\u53EA\u770B\u5F97\u5230\u6761\u4EF6\uFF0C\u770B\u4E0D\u5230\u753B\u9762\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(GlassButton, { size: "sm", children: "\u5237\u65B0" })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 3, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(BodySm, { children: "\u8FD8\u6CA1\u6709 CG\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u5728\u5267\u60C5\u91CC\u89E6\u53D1\u7B2C\u4E00\u5F20\u4E4B\u540E\uFF0C\u8FD9\u91CC\u4F1A\u6309\u7C7B\u522B\u5217\u51FA\u5B83\u7684\u65F6\u957F\u3001\u5927\u5C0F\u3001\u5173\u8054\u573A\u666F\u4E0E\u5F15\u5165\u6761\u4EF6\u3002" })
    ] })
  ] });
}
function TalkTab({ ctx }) {
  const { talkLines, talkInput, talkHint, setTalkInput, talkSend, talkClear } = ctx;
  return /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 4, children: [
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(SectionHead, { no: "06", eyebrow: "// REPLY", title: "\u5148\u542C\u542C TA \u600E\u4E48\u63A5\u8BDD" }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u8FD9\u51E0\u53E5\u53EA\u6309\u4F60\u73B0\u5728\u586B\u7684\u8BBE\u5B9A\u56DE\u7B54\uFF08\u8FD8\u6CA1\u4FDD\u5B58\u7684\u4E5F\u7B97\uFF09\uFF0C\u4E0D\u5199\u8FDB TA \u7684\u8BB0\u5FC6\u3001\u4E0D\u8FDB\u5B58\u6863\u3002" }),
    talkLines.length === 0 ? /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 1, children: [
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(BodySm, { children: "\u8FD8\u6CA1\u5F00\u53E3\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u8BD5\u4E00\u53E5\u5E73\u65F6\u4F1A\u8BF4\u7684\u8BDD\uFF0C\u6BD4\u5982\u300C\u4ECA\u5929\u600E\u4E48\u8FD9\u4E48\u65E9\u300D\u2014\u2014\u53E3\u6C14\u5BF9\u4E0D\u5BF9\uFF0C\u4E00\u53E5\u5C31\u80FD\u542C\u51FA\u6765\u3002" })
    ] }) : /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Stack, { gap: 3, children: talkLines.map(
      (line) => line.fromMe ? /* @__PURE__ */ (0, import_jsx_runtime19.jsx)("div", { style: { display: "flex", justifyContent: "flex-end" }, children: /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
        "div",
        {
          style: {
            maxWidth: 380,
            padding: "var(--w-space-2) var(--w-space-3)",
            borderRadius: 4,
            background: "var(--w-surface-tint)"
          },
          children: /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(BodySm, { children: line.text })
        }
      ) }, line.id) : /* @__PURE__ */ (0, import_jsx_runtime19.jsx)("div", { style: { display: "flex", justifyContent: "flex-start" }, children: /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
        "div",
        {
          style: {
            maxWidth: 380,
            padding: "var(--w-space-2) var(--w-space-3)",
            borderRadius: 4,
            background: "var(--w-tint-card)",
            border: "1px solid var(--w-hairline)"
          },
          children: /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(Stack, { gap: 0, children: [
            /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: line.who }),
            /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(BodySm, { children: line.text })
          ] })
        }
      ) }, line.id)
    ) }),
    /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)(
      "div",
      {
        style: {
          borderRadius: 4,
          border: "1px solid var(--w-hairline)",
          background: "var(--w-surface-tint)",
          borderLeft: "2px solid var(--w-signal)",
          padding: "8px 12px",
          display: "flex",
          flexDirection: "column",
          gap: 6
        },
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Eyebrow, { children: "// SAY" }),
            /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: "\u56DE\u8F66\u9001\u51FA" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-3)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime19.jsx)("span", { style: { fontFamily: "var(--w-font-num)", fontSize: 18, color: "var(--w-ink-faint)" }, children: "\u203A" }),
            /* @__PURE__ */ (0, import_jsx_runtime19.jsx)("div", { style: { flex: 1, minWidth: 0 }, children: /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(
              Field,
              {
                value: talkInput,
                onChange: setTalkInput,
                placeholder: "\u60F3\u5BF9 TA \u8BF4\u7684\u8BDD"
              }
            ) }),
            /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(PrimaryBtn, { onClick: talkSend, children: "\u8BF4\u51FA\u53BB \u2192" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime19.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(Caption, { children: talkHint }),
            talkLines.length > 0 ? /* @__PURE__ */ (0, import_jsx_runtime19.jsx)(TextBtn, { onClick: talkClear, children: "\u64E6\u6389\u8FD9\u51E0\u53E5" }) : null
          ] })
        ]
      }
    )
  ] });
}

// src/screens/character/subProfile.tsx
var import_react15 = __toESM(require_react(), 1);
var import_jsx_runtime20 = __toESM(require_jsx_runtime(), 1);
var MAIN_PERSONALITY = "\u6E29\u67D4\u53EF\u7231";
var MAIN_DESCRIPTION = "\u521A\u521A\u8BDE\u751F\u7684\u4F19\u4F34\uFF0C\u4E00\u5207\u90FD\u662F\u5D2D\u65B0\u7684\u5F00\u59CB\u3002";
var SEEDED_TONE = "\u5BF9\u4F60\u8FD8\u6709\u4E9B\u751F\u758F\uFF0C\u8BF4\u8BDD\u4F1A\u7559\u4E00\u70B9\u8DDD\u79BB\uFF1B\u5FC3\u91CC\u5BF9\u4F60\u8FD8\u6709\u4FDD\u7559\u3002";
function SubProfile({ back }) {
  const [status, setStatus] = (0, import_react15.useState)("");
  const reset = () => {
    const yes = window.confirm("\u628A\u526F\u8BBE\u5B9A\u9000\u56DE\u4E3B\u8BBE\u5B9A\u7684\u526F\u672C\uFF1F\u4F1A\u6E05\u7A7A\u7D2F\u79EF\u7684\u5FC3\u5883\u3001\u5FC3\u4E8B\u4E0E\u7559\u5F71\uFF0C\u4E3B\u8BBE\u5B9A\u4E0D\u53D7\u5F71\u54CD\u3002");
    if (!yes) return;
    setStatus("\u5DF2\u56DE\u6B63\u5230\u4E3B\u8BBE\u5B9A\u3002");
  };
  return /* @__PURE__ */ (0, import_jsx_runtime20.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime20.jsx)(PageHeader, { title: "\u4E3B\u8BBE\u5B9A \xD7 \u526F\u8BBE\u5B9A", onBack: back }),
    /* @__PURE__ */ (0, import_jsx_runtime20.jsx)("div", { className: "screen", style: { maxWidth: 860 }, children: /* @__PURE__ */ (0, import_jsx_runtime20.jsxs)(Stack, { gap: 4, children: [
      /* @__PURE__ */ (0, import_jsx_runtime20.jsxs)(Stack, { gap: 1, children: [
        /* @__PURE__ */ (0, import_jsx_runtime20.jsx)(
          "h2",
          {
            style: {
              margin: 0,
              fontSize: "var(--w-size-display)",
              fontWeight: 600,
              color: "var(--w-ink-display)",
              lineHeight: 1.35
            },
            children: "\u5C0F\u96E8\uFF1A\u4E3B\u8BBE\u5B9A \xD7 \u526F\u8BBE\u5B9A"
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime20.jsx)(Caption, { children: "\u5DE6\u8FB9\u662F\u4E3B\u8BBE\u5B9A\uFF08\u89D2\u8272\u5E93\u89C4\u5B9A\uFF0C\u8FD0\u884C\u65F6\u4E0D\u6539\uFF09\uFF1B\u53F3\u8FB9\u662F\u526F\u8BBE\u5B9A\uFF08\u968F\u7ECF\u5386\u6F02\u79FB\u7684\u5F53\u4E0B\u4E4B\u6211\uFF09\u3002" }),
        /* @__PURE__ */ (0, import_jsx_runtime20.jsx)("span", { style: { fontSize: "var(--w-size-small)", color: "var(--w-ink-body)" }, children: "\u8FD8\u6CA1\u6709\u6F02\u79FB\u8FC7\u2014\u2014TA \u5C31\u662F\u4E3B\u8BBE\u5B9A\u91CC\u7684\u6837\u5B50\u3002" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime20.jsxs)(Grid2, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime20.jsxs)(Glass, { as: "section", tone: "card", radius: 12, style: { padding: 0 }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime20.jsx)(Row2, { title: "\u4E3B\u8BBE\u5B9A\uFF08\u5185\u6838\uFF09" }),
          /* @__PURE__ */ (0, import_jsx_runtime20.jsx)(Hair, {}),
          /* @__PURE__ */ (0, import_jsx_runtime20.jsx)(Row2, { title: "\u6027\u683C", body: MAIN_PERSONALITY }),
          /* @__PURE__ */ (0, import_jsx_runtime20.jsx)(Hair, {}),
          /* @__PURE__ */ (0, import_jsx_runtime20.jsx)(Row2, { title: "\u80CC\u666F", body: MAIN_DESCRIPTION })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime20.jsxs)(Glass, { as: "section", tone: "card", radius: 12, style: { padding: 0 }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime20.jsx)(Row2, { title: "\u526F\u8BBE\u5B9A\uFF08\u5F53\u4E0B\u4E4B\u6211\uFF09" }),
          /* @__PURE__ */ (0, import_jsx_runtime20.jsx)(Hair, {}),
          /* @__PURE__ */ (0, import_jsx_runtime20.jsx)(Row2, { title: "\u6027\u683C", body: MAIN_PERSONALITY, note: "\u4E0E\u4E3B\u8BBE\u5B9A\u4E00\u81F4" }),
          /* @__PURE__ */ (0, import_jsx_runtime20.jsx)(Hair, {}),
          /* @__PURE__ */ (0, import_jsx_runtime20.jsx)(Row2, { title: "\u80CC\u666F", body: MAIN_DESCRIPTION, note: "\u4E0E\u4E3B\u8BBE\u5B9A\u4E00\u81F4" }),
          /* @__PURE__ */ (0, import_jsx_runtime20.jsx)(Hair, {}),
          /* @__PURE__ */ (0, import_jsx_runtime20.jsx)(Row2, { title: "\u6B64\u523B\u5FC3\u5883", value: SEEDED_TONE })
        ] })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime20.jsxs)(Glass, { as: "section", tone: "card", radius: 12, style: { padding: 0 }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime20.jsx)(Row2, { title: "\u8FD1\u671F\u5FC3\u4E8B\uFF08TA \u81EA\u5DF1\u653E\u5728\u5FC3\u4E0A\u7684\u4E8B\uFF09" }),
        /* @__PURE__ */ (0, import_jsx_runtime20.jsx)(Hair, {}),
        /* @__PURE__ */ (0, import_jsx_runtime20.jsx)("div", { style: { padding: "10px var(--w-pad-card)" }, children: /* @__PURE__ */ (0, import_jsx_runtime20.jsxs)(Stack, { gap: 1, children: [
          /* @__PURE__ */ (0, import_jsx_runtime20.jsx)("span", { style: { fontSize: "var(--w-size-body)", color: "var(--w-ink-body)" }, children: "\u6682\u65E0\u5FC3\u4E8B" }),
          /* @__PURE__ */ (0, import_jsx_runtime20.jsx)(Caption, { children: "\u5FC3\u4E8B\u7531\u6BCF\u6B21\u6298\u53E0\u51FA\u7684\u8BB0\u5FC6\u8981\u70B9\u7D2F\u79EF\u800C\u6765\uFF0C\u6700\u591A\u4FDD\u7559 8 \u6761\u3001\u7559\u5F71 12 \u4EFD\u3002" })
        ] }) })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime20.jsxs)("div", { style: { display: "flex", justifyContent: "center", gap: "var(--w-space-3)", alignItems: "center" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime20.jsx)(GlassButton, { size: "sm", children: "\u5237\u65B0" }),
        /* @__PURE__ */ (0, import_jsx_runtime20.jsx)(DangerBtn, { onClick: reset, children: "\u526F\u8BBE\u5B9A\u56DE\u6B63" })
      ] }),
      status ? /* @__PURE__ */ (0, import_jsx_runtime20.jsx)(Note, { children: status }) : null,
      /* @__PURE__ */ (0, import_jsx_runtime20.jsx)(Note, { children: "\u5728\u8BBE\u7F6E\u91CC\u6253\u5F00\u300C\u8BA9\u6A21\u578B\u53C2\u4E0E\u526F\u8BBE\u5B9A\u6539\u5199\u300D\u540E\uFF0CTA \u4F1A\u6BCF 12 \u8F6E\u5BF9\u8BDD\u91CD\u65B0\u8868\u8FF0\u4E00\u6B21\u300C\u81EA\u5DF1\u73B0\u5728\u7684\u6837\u5B50\u300D\uFF08\u9ED8\u8BA4\u5173\u95ED\uFF0C\u56E0\u4E3A\u8981\u82B1\u94B1\uFF09\u3002" })
    ] }) })
  ] });
}
function Row2({ title: title2, body, note, value }) {
  return /* @__PURE__ */ (0, import_jsx_runtime20.jsxs)(
    "div",
    {
      style: {
        display: "flex",
        alignItems: "center",
        gap: "var(--w-space-3)",
        padding: "10px var(--w-pad-card)"
      },
      children: [
        /* @__PURE__ */ (0, import_jsx_runtime20.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 2, flex: 1, minWidth: 0 }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime20.jsx)("span", { style: { fontSize: "var(--w-size-body)", color: "var(--w-ink-body)" }, children: title2 }),
          body ? /* @__PURE__ */ (0, import_jsx_runtime20.jsx)(Caption, { children: body }) : null
        ] }),
        note ? /* @__PURE__ */ (0, import_jsx_runtime20.jsx)("span", { style: { fontSize: "var(--w-size-caption)", color: "var(--w-signal)", textAlign: "right" }, children: note }) : null,
        value ? /* @__PURE__ */ (0, import_jsx_runtime20.jsx)("span", { style: { fontSize: "var(--w-size-small)", color: "var(--w-ink-body)", textAlign: "right", maxWidth: "55%" }, children: value }) : null
      ]
    }
  );
}

// src/screens/character/index.tsx
var screens2 = {
  roster: Roster,
  studio: Studio,
  newcharacter: NewCharacter,
  subProfile: SubProfile
};

// src/screens/games/affection-level-up.tsx
var import_jsx_runtime21 = __toESM(require_jsx_runtime(), 1);
var TITLES = ["\u521D\u8BC6", "\u76F8\u8BC6", "\u719F\u6089", "\u4EB2\u8FD1", "\u5FC3\u52A8", "\u6E29\u67D4", "\u4F9D\u604B", "\u6DF1\u60C5", "\u631A\u7231", "\u6C38\u6052"];
function AffectionLevelUp({ back, query }) {
  const level = Number(query.get("level") ?? "1");
  const safeLevel = Number.isFinite(level) ? Math.min(10, Math.max(1, Math.floor(level))) : 1;
  const points = Number(query.get("points") ?? "0");
  const name = query.get("name") ?? "\u5C0F\u96E8";
  const title2 = TITLES[safeLevel - 1];
  return /* @__PURE__ */ (0, import_jsx_runtime21.jsxs)(
    "div",
    {
      role: "button",
      tabIndex: 0,
      onClick: back,
      onKeyDown: (event) => {
        if (event.key === "Enter" || event.key === " ") back();
      },
      "aria-label": "\u8F7B\u89E6\u4EFB\u610F\u5904\u8FD4\u56DE",
      style: {
        position: "relative",
        display: "block",
        width: "100%",
        height: "100%",
        minHeight: "100%",
        border: 0,
        padding: 0,
        overflow: "hidden",
        cursor: "pointer",
        background: "color-mix(in oklab, var(--w-signal) 42%, var(--w-bg))",
        color: "var(--w-ink-on-signal)",
        font: "inherit",
        textAlign: "center"
      },
      children: [
        /* @__PURE__ */ (0, import_jsx_runtime21.jsx)(
          "span",
          {
            "aria-hidden": "true",
            style: {
              position: "absolute",
              inset: 0,
              background: "var(--w-signal)",
              opacity: 0.55
            }
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime21.jsxs)("div", { style: { position: "absolute", top: 70, left: 0, right: 0 }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime21.jsx)("div", { style: { height: 74, background: "var(--w-signal)", opacity: 0.96 } }),
          /* @__PURE__ */ (0, import_jsx_runtime21.jsx)(
            "div",
            {
              style: {
                position: "absolute",
                top: 2,
                left: 0,
                right: 0,
                height: 30,
                background: "color-mix(in oklab, var(--w-signal) 55%, var(--w-bg))",
                opacity: 0.9
              }
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime21.jsxs)(
            "div",
            {
              style: {
                position: "absolute",
                top: 14,
                left: 0,
                right: 0,
                display: "flex",
                flexDirection: "column",
                gap: 2
              },
              children: [
                /* @__PURE__ */ (0, import_jsx_runtime21.jsx)("span", { style: { fontSize: 30, fontWeight: 700 }, children: "\u597D\u611F\u7B49\u7EA7\u63D0\u5347\uFF01" }),
                /* @__PURE__ */ (0, import_jsx_runtime21.jsxs)(
                  "span",
                  {
                    style: {
                      fontSize: 17,
                      color: "color-mix(in oklab, var(--w-ink-on-signal) 82%, var(--w-signal))",
                      whiteSpace: "nowrap",
                      overflow: "hidden",
                      textOverflow: "ellipsis",
                      padding: "0 16px"
                    },
                    children: [
                      "\u300C",
                      title2,
                      "\u300D \xB7 ",
                      name
                    ]
                  }
                )
              ]
            }
          )
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime21.jsx)(
          "div",
          {
            style: {
              position: "absolute",
              left: "50%",
              top: "50%",
              transform: "translate(-50%, -50%)",
              marginTop: -60
            },
            children: /* @__PURE__ */ (0, import_jsx_runtime21.jsx)(Glass, { tone: "card", radius: 32, style: { padding: "24px 30px", border: "2px solid var(--w-ink-on-signal)" }, children: /* @__PURE__ */ (0, import_jsx_runtime21.jsxs)("div", { style: { display: "flex", flexDirection: "column", alignItems: "center", gap: "var(--w-space-1)" }, children: [
              /* @__PURE__ */ (0, import_jsx_runtime21.jsx)(
                "div",
                {
                  "aria-hidden": "true",
                  style: {
                    width: 52,
                    height: 52,
                    borderRadius: 8,
                    border: "1px dashed var(--w-ink-on-signal)",
                    opacity: 0.7
                  }
                }
              ),
              /* @__PURE__ */ (0, import_jsx_runtime21.jsxs)("span", { style: { fontSize: 44, fontWeight: 700, lineHeight: 1.1 }, children: [
                "Lv.",
                safeLevel
              ] }),
              /* @__PURE__ */ (0, import_jsx_runtime21.jsx)("span", { style: { fontSize: 16, whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }, children: name }),
              /* @__PURE__ */ (0, import_jsx_runtime21.jsxs)("span", { style: { fontSize: 12, opacity: 0.85 }, children: [
                Number.isFinite(points) ? Math.min(1e3, Math.max(0, Math.floor(points))) : 0,
                "/1000 \xB7 \u8DE8\u8FC7\u4E86 ",
                safeLevel * 100,
                " \u70B9"
              ] })
            ] }) })
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime21.jsx)(
          "div",
          {
            "aria-hidden": "true",
            style: {
              position: "absolute",
              bottom: 0,
              left: "50%",
              transform: "translateX(-50%)",
              width: 260,
              maxWidth: "70%",
              height: 300,
              border: "1px dashed color-mix(in oklab, var(--w-ink-on-signal) 55%, transparent)",
              borderRadius: 12,
              opacity: 0.5
            }
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime21.jsx)(
          "span",
          {
            style: {
              position: "absolute",
              left: 0,
              right: 0,
              bottom: 22,
              fontSize: 13,
              color: "var(--w-ink-on-signal)",
              opacity: 0.75
            },
            children: "\u8F7B\u89E6\u4EFB\u610F\u5904\u8FD4\u56DE"
          }
        )
      ]
    }
  );
}

// src/screens/games/cg-view.tsx
var import_jsx_runtime22 = __toESM(require_jsx_runtime(), 1);
function CgView({ back, query }) {
  const title2 = query.get("title") ?? "";
  return /* @__PURE__ */ (0, import_jsx_runtime22.jsxs)(
    "div",
    {
      style: {
        position: "relative",
        width: "100%",
        height: "100%",
        minHeight: "100%",
        background: "var(--w-ink-display)",
        overflow: "hidden"
      },
      children: [
        /* @__PURE__ */ (0, import_jsx_runtime22.jsx)(
          "div",
          {
            "aria-label": "CG",
            style: {
              position: "absolute",
              left: "50%",
              top: "50%",
              transform: "translate(-50%, -50%)",
              width: "calc(100% - 96px)",
              height: "calc(100% - 140px)",
              border: "1px dashed color-mix(in oklab, var(--w-ink-on-signal) 45%, transparent)",
              borderRadius: 10
            }
          }
        ),
        title2 ? /* @__PURE__ */ (0, import_jsx_runtime22.jsx)(
          "span",
          {
            style: {
              position: "absolute",
              left: 16,
              right: 16,
              bottom: 30,
              textAlign: "center",
              fontSize: "var(--w-size-body)",
              color: "var(--w-ink-on-signal)",
              opacity: 0.75,
              whiteSpace: "nowrap",
              overflow: "hidden",
              textOverflow: "ellipsis"
            },
            children: title2
          }
        ) : null,
        /* @__PURE__ */ (0, import_jsx_runtime22.jsx)(
          "button",
          {
            type: "button",
            "aria-label": "\u8DF3\u8FC7",
            onClick: back,
            style: {
              position: "absolute",
              left: 18,
              top: 54,
              padding: "10px 18px",
              border: 0,
              borderRadius: "var(--w-radius-button)",
              background: "color-mix(in oklab, var(--w-ink-on-signal) 22%, transparent)",
              color: "var(--w-ink-on-signal)",
              font: "inherit",
              fontSize: "var(--w-size-title)",
              cursor: "pointer",
              lineHeight: 1
            },
            children: "\u2715"
          }
        )
      ]
    }
  );
}

// src/screens/games/flows.tsx
var import_react16 = __toESM(require_react(), 1);

// src/screens/games/parts.tsx
var import_jsx_runtime23 = __toESM(require_jsx_runtime(), 1);
function Section({
  children,
  muted: muted3,
  style
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime23.jsx)(
    "span",
    {
      style: {
        fontSize: muted3 ? "var(--w-size-caption)" : "var(--w-size-subhead)",
        fontWeight: muted3 ? 400 : 600,
        letterSpacing: muted3 ? "0.04em" : void 0,
        color: muted3 ? "var(--w-ink-muted)" : "var(--w-ink-display)",
        ...style
      },
      children
    }
  );
}
function Cap2({
  children,
  tone = "muted",
  style
}) {
  const color = tone === "body" ? "var(--w-ink-body)" : tone === "signal" ? "var(--w-signal)" : tone === "faint" ? "var(--w-ink-faint)" : "var(--w-ink-muted)";
  return /* @__PURE__ */ (0, import_jsx_runtime23.jsx)(
    "span",
    {
      style: {
        fontSize: "var(--w-size-caption)",
        color,
        lineHeight: 1.6,
        ...style
      },
      children
    }
  );
}
function Well3({
  children,
  active,
  onClick,
  style
}) {
  const base = {
    display: "inline-flex",
    alignItems: "center",
    padding: "7px 10px",
    borderRadius: "var(--w-radius-well)",
    border: 0,
    font: "inherit",
    fontSize: "var(--w-size-small)",
    fontWeight: active ? 600 : 400,
    color: active ? "var(--w-ink-on-signal)" : "var(--w-ink-body)",
    background: active ? "var(--w-signal)" : "var(--w-surface-tint)",
    cursor: onClick ? "pointer" : "default",
    ...style
  };
  if (!onClick) return /* @__PURE__ */ (0, import_jsx_runtime23.jsx)("span", { style: base, children });
  return /* @__PURE__ */ (0, import_jsx_runtime23.jsx)("button", { type: "button", style: base, "aria-pressed": active, onClick, children });
}
function Fab({
  label,
  onClick,
  size = 44,
  fontSize = "var(--w-size-body)",
  title: title2
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime23.jsx)(
    "button",
    {
      type: "button",
      "aria-label": title2 ?? label,
      onClick,
      style: {
        width: size,
        height: size,
        display: "inline-flex",
        alignItems: "center",
        justifyContent: "center",
        borderRadius: 99,
        border: "1px solid var(--w-hairline)",
        background: "var(--w-surface-tint)",
        color: "var(--w-ink-body)",
        font: "inherit",
        fontSize,
        cursor: "pointer"
      },
      children: label
    }
  );
}
function FormRow({
  title: title2,
  desc: desc2,
  right,
  children
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime23.jsxs)(
    "div",
    {
      style: {
        display: "flex",
        flexWrap: "wrap",
        alignItems: "center",
        justifyContent: "space-between",
        gap: "var(--w-space-2)",
        minHeight: "var(--w-row-h)",
        padding: "10px 0"
      },
      children: [
        title2 || desc2 ? /* @__PURE__ */ (0, import_jsx_runtime23.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 2, flex: "1 1 220px", minWidth: 0 }, children: [
          title2 ? /* @__PURE__ */ (0, import_jsx_runtime23.jsx)("span", { style: { fontSize: "var(--w-size-body)", color: "var(--w-ink-body)" }, children: title2 }) : null,
          desc2 ? /* @__PURE__ */ (0, import_jsx_runtime23.jsx)(Cap2, { children: desc2 }) : null
        ] }) : null,
        right ? /* @__PURE__ */ (0, import_jsx_runtime23.jsx)("div", { style: { display: "inline-flex", alignItems: "center", gap: "var(--w-space-1)" }, children: right }) : null,
        children
      ]
    }
  );
}
function ToolRow({ children }) {
  return /* @__PURE__ */ (0, import_jsx_runtime23.jsx)("div", { style: { display: "flex", flexWrap: "wrap", alignItems: "center", gap: "var(--w-space-2)" }, children });
}
function InlineField({
  value,
  onChange,
  placeholder,
  onEnter,
  fontSize = "var(--w-size-small)",
  style
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime23.jsx)(
    "input",
    {
      type: "text",
      className: "k-field",
      value,
      placeholder,
      "aria-label": placeholder,
      onChange: (event) => onChange(event.target.value),
      onKeyDown: (event) => {
        if (event.key === "Enter" && onEnter) onEnter();
      },
      style: { fontSize, padding: "7px 10px", ...style }
    }
  );
}

// src/screens/games/flows.tsx
var import_jsx_runtime24 = __toESM(require_jsx_runtime(), 1);
var SEED2 = [
  {
    id: "seed",
    name: "\u5929\u9ED1\u4E86\u5F00\u95E8\u53E3\u7684\u706F",
    enabled: true,
    trigger: "\u592A\u9633\u6362\u6863\uFF08\u9ED1\u5929\uFF09",
    when: "\u5F53 \u73B0\u5728\u662F\u9ED1\u5929",
    then: "\u90A3\u4E48 \u628A\u300C\u95E8\u53E3\u7684\u706F\u300D\u6539\u6210\u300C\u5F00\u300D"
  }
];
function Flows({ back }) {
  const [rows2, setRows] = (0, import_react16.useState)(SEED2);
  const [status, setStatus] = (0, import_react16.useState)(`\u5171 ${SEED2.length} \u6761\u89C4\u5219\uFF0C\u6539\u5B8C\u7ACB\u523B\u751F\u6548\uFF08\u4E0D\u7528\u91CD\u542F\uFF09\u3002`);
  const refresh = (next, message) => {
    setRows(next);
    setStatus(next.length === 0 ? "\u8FD8\u6CA1\u6709\u89C4\u5219\u3002\u70B9\u300C\uFF0B \u65B0\u89C4\u5219\u300D\u5199\u4E00\u6761\uFF1B\u591A\u5C42\u4E0E/\u6216\u7684\u6761\u4EF6\u53EF\u4EE5\u8BA9 AI \u5E2E\u4F60\u5199\u3002" : message);
  };
  const add = () => {
    const name = window.prompt("\u65B0\u89C4\u5219\n\u7ED9\u8FD9\u6761\u89C4\u5219\u8D77\u4E2A\u540D\u5B57\uFF08\u4F8B\uFF1A\u5929\u9ED1\u4E86\u5F00\u95E8\u53E3\u7684\u706F\uFF09");
    if (name === null) return;
    const what = window.prompt("\u63D0\u793A\u4EC0\u4E48\uFF1F\n\u4E00\u53E5\u8BDD\uFF08\u4F1A\u4EE5 Toast \u5F39\u51FA\u6765\uFF09");
    if (what === null) return;
    const rule = {
      id: `r${rows2.length + 1}`,
      name: name.trim(),
      enabled: true,
      trigger: "\u624B\u52A8",
      when: "\u5F53 \u53EA\u8981\u89E6\u53D1",
      then: `\u90A3\u4E48 \u8BF4\u4E00\u53E5\uFF1A${what.trim()}`
    };
    refresh([...rows2, rule], `\u5171 ${rows2.length + 1} \u6761\u89C4\u5219\uFF0C\u6539\u5B8C\u7ACB\u523B\u751F\u6548\uFF08\u4E0D\u7528\u91CD\u542F\uFF09\u3002`);
  };
  const edit = (row) => {
    const name = window.prompt("\u6539\u89C4\u5219\n\u8FD9\u6761\u89C4\u5219\u53EB\u4EC0\u4E48\uFF1F", row.name);
    if (name === null) return;
    refresh(
      rows2.map((item) => item.id === row.id ? { ...item, name: name.trim() } : item),
      "\u89C4\u5219\u5DF2\u6539\u597D\u3002"
    );
  };
  const toggle = (row) => {
    const enabled = !row.enabled;
    refresh(
      rows2.map((item) => item.id === row.id ? { ...item, enabled } : item),
      enabled ? "\u5DF2\u542F\u7528\u3002" : "\u5DF2\u505C\u7528\uFF1A\u8FD9\u6761\u89C4\u5219\u4E0D\u518D\u81EA\u52A8\u89E6\u53D1\uFF0C\u624B\u52A8\u8DD1\u4ECD\u53EF\u7528\u3002"
    );
  };
  const remove = (row) => {
    if (!window.confirm(`\u5220\u6389\u8FD9\u6761\u89C4\u5219\uFF1F
${row.name}`)) return;
    refresh(rows2.filter((item) => item.id !== row.id), "\u5DF2\u5220\u6389\u3002");
  };
  return /* @__PURE__ */ (0, import_jsx_runtime24.jsx)(PageShell, { children: /* @__PURE__ */ (0, import_jsx_runtime24.jsxs)("div", { className: "screen", style: { gap: "var(--w-space-3)" }, children: [
    /* @__PURE__ */ (0, import_jsx_runtime24.jsx)(Glass, { tone: "card", radius: 12, children: /* @__PURE__ */ (0, import_jsx_runtime24.jsxs)(
      "div",
      {
        style: {
          display: "flex",
          flexWrap: "wrap",
          alignItems: "center",
          justifyContent: "space-between",
          gap: "var(--w-space-2)",
          padding: "var(--w-space-2)"
        },
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime24.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 3, minWidth: 0 }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime24.jsx)("span", { style: { fontSize: "var(--w-size-title)", fontWeight: 600, color: "var(--w-ink-display)" }, children: "\u6D41\u7A0B\u56FE" }),
            /* @__PURE__ */ (0, import_jsx_runtime24.jsx)(Cap2, { children: status })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime24.jsxs)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-2)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime24.jsx)(GlassButton, { size: "sm", onClick: add, children: "\uFF0B \u65B0\u89C4\u5219" }),
            /* @__PURE__ */ (0, import_jsx_runtime24.jsx)(GlassButton, { size: "sm", onClick: back, children: "\u8FD4\u56DE" })
          ] })
        ]
      }
    ) }),
    /* @__PURE__ */ (0, import_jsx_runtime24.jsx)("div", { style: { display: "flex", flexDirection: "column" }, children: rows2.map((row) => /* @__PURE__ */ (0, import_jsx_runtime24.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-1)", padding: "10px 4px" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime24.jsxs)(
        "div",
        {
          style: {
            display: "grid",
            gridTemplateColumns: "minmax(0, 1fr) auto",
            columnGap: "var(--w-space-3)",
            rowGap: "var(--w-space-1)",
            alignItems: "center"
          },
          children: [
            /* @__PURE__ */ (0, import_jsx_runtime24.jsx)(
              "span",
              {
                style: {
                  fontSize: "var(--w-size-body)",
                  color: "var(--w-ink-body)",
                  whiteSpace: "nowrap",
                  overflow: "hidden",
                  textOverflow: "ellipsis"
                },
                children: row.name
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime24.jsx)(Cap2, { style: { whiteSpace: "nowrap" }, children: row.enabled ? "\u25CF \u542F\u7528" : "\u25CB \u505C\u7528" }),
            /* @__PURE__ */ (0, import_jsx_runtime24.jsx)(Cap2, { style: { gridColumn: "1 / -1", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }, children: row.trigger }),
            /* @__PURE__ */ (0, import_jsx_runtime24.jsx)("span", { style: { gridColumn: "1 / -1", fontSize: "var(--w-size-small)", color: "var(--w-ink-body)", lineHeight: 1.55 }, children: row.when }),
            /* @__PURE__ */ (0, import_jsx_runtime24.jsx)("span", { style: { fontSize: "var(--w-size-small)", color: "var(--w-ink-body)", lineHeight: 1.55 }, children: row.then }),
            /* @__PURE__ */ (0, import_jsx_runtime24.jsxs)("div", { style: { display: "flex", alignItems: "flex-end", gap: "var(--w-space-1)" }, children: [
              /* @__PURE__ */ (0, import_jsx_runtime24.jsx)(GlassButton, { size: "sm", onClick: () => setStatus(`\u300C${row.name}\u300D\u8DD1\u5B8C\u4E86\u3002`), children: "\u8BD5\u8DD1" }),
              /* @__PURE__ */ (0, import_jsx_runtime24.jsx)(GlassButton, { size: "sm", onClick: () => edit(row), children: "\u6539" }),
              /* @__PURE__ */ (0, import_jsx_runtime24.jsx)(GlassButton, { size: "sm", onClick: () => toggle(row), children: "\u542F\u505C" }),
              /* @__PURE__ */ (0, import_jsx_runtime24.jsx)(GlassButton, { size: "sm", onClick: () => remove(row), children: "\u5220" })
            ] })
          ]
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime24.jsx)("div", { className: "k-sep", style: { margin: "8px 0 0" }, role: "presentation" })
    ] }, row.id)) })
  ] }) });
}

// src/screens/games/game.tsx
var import_react17 = __toESM(require_react(), 1);
var import_jsx_runtime25 = __toESM(require_jsx_runtime(), 1);
var SIZE = 15;
var CELL = 30;
var PAD = 16;
var SPAN = (SIZE - 1) * CELL;
var BOARD = SPAN + PAD * 2;
var STARS = [
  [3, 3],
  [3, 11],
  [11, 3],
  [11, 11],
  [7, 7]
];
var WIN_TEXT_MINE = "\u4F60\u8D62\u4E86\uFF01\u4E94\u5B50\u8FDE\u73E0";
var WIN_TEXT_HERS = "\u5C0F\u96E8\u8D62\u4E86\u2026\u4E94\u5B50\u8FDE\u73E0";
var WIN_TEXT_DRAW = "\u5E73\u5C40\uFF0C\u68CB\u76D8\u5DF2\u6EE1";
function hasFive(stones, last) {
  const at = (r, c) => stones.some((s) => s.r === r && s.c === c && s.black === last.black);
  return [
    [1, 0],
    [0, 1],
    [1, 1],
    [1, -1]
  ].some(([dr, dc]) => {
    let count = 1;
    for (const sign of [1, -1]) {
      let r = last.r + dr * sign;
      let c = last.c + dc * sign;
      while (at(r, c)) {
        count += 1;
        r += dr * sign;
        c += dc * sign;
      }
    }
    return count >= 5;
  });
}
function Game({ back }) {
  const [stones, setStones] = (0, import_react17.useState)([]);
  const [black, setBlack] = (0, import_react17.useState)(true);
  const [display2, setDisplay] = (0, import_react17.useState)("\u8F6E\u5230\u4F60\u8D70");
  const [input, setInput] = (0, import_react17.useState)("");
  const [messages, setMessages] = (0, import_react17.useState)(["\u5C0F\u96E8: \u4E94\u5B50\u68CB\u5F00\u59CB\uFF01\u70B9\u51FB\u7A7A\u4F4D\u843D\u5B50\uFF0C\u8FDE\u6210\u4E94\u5B50\u5373\u80DC"]);
  const place = (r, c) => {
    if (stones.length >= SIZE * SIZE) return;
    if (stones.some((s) => s.r === r && s.c === c)) return;
    const next = { r, c, black };
    const list = [...stones, next];
    setStones(list);
    if (hasFive(list, next)) {
      setDisplay(black ? WIN_TEXT_MINE : WIN_TEXT_HERS);
      return;
    }
    if (list.length === SIZE * SIZE) {
      setDisplay(WIN_TEXT_DRAW);
      return;
    }
    setBlack(!black);
  };
  const send = () => {
    const text = input.trim();
    if (!text) return;
    setMessages([...messages, `\u4F60: ${text}`]);
    setInput("");
  };
  return /* @__PURE__ */ (0, import_jsx_runtime25.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime25.jsx)(
      PageHeader,
      {
        title: "\u4E94\u5B50\u68CB",
        onBack: back,
        right: /* @__PURE__ */ (0, import_jsx_runtime25.jsx)("span", { style: { fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)" }, children: "\u5C0F\u96E8" })
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime25.jsxs)(
      "div",
      {
        style: {
          display: "flex",
          flexWrap: "wrap",
          alignItems: "stretch",
          gap: "var(--w-space-3)",
          padding: "var(--w-space-3) var(--w-pad-page)",
          minHeight: "calc(100% - 57px)",
          boxSizing: "border-box"
        },
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime25.jsxs)(
            "div",
            {
              style: {
                display: "flex",
                flexDirection: "column",
                alignItems: "center",
                gap: 6,
                padding: "8px 0 0 8px"
              },
              children: [
                /* @__PURE__ */ (0, import_jsx_runtime25.jsx)(
                  "span",
                  {
                    style: {
                      fontSize: "var(--w-size-caption)",
                      color: "var(--w-ink-muted)",
                      maxWidth: BOARD,
                      whiteSpace: "nowrap",
                      overflow: "hidden",
                      textOverflow: "ellipsis"
                    },
                    children: display2
                  }
                ),
                /* @__PURE__ */ (0, import_jsx_runtime25.jsxs)(
                  "div",
                  {
                    style: {
                      position: "relative",
                      width: BOARD,
                      height: BOARD,
                      boxSizing: "border-box",
                      borderRadius: 10,
                      border: "1px solid var(--w-edge)",
                      background: "var(--w-surface-tint)"
                    },
                    children: [
                      Array.from({ length: SIZE }, (_, i) => /* @__PURE__ */ (0, import_jsx_runtime25.jsx)(
                        "span",
                        {
                          "aria-hidden": "true",
                          style: {
                            position: "absolute",
                            left: PAD,
                            top: PAD + i * CELL,
                            width: SPAN,
                            height: 1,
                            background: "var(--w-edge)"
                          }
                        },
                        `h${i}`
                      )),
                      Array.from({ length: SIZE }, (_, i) => /* @__PURE__ */ (0, import_jsx_runtime25.jsx)(
                        "span",
                        {
                          "aria-hidden": "true",
                          style: {
                            position: "absolute",
                            top: PAD,
                            left: PAD + i * CELL,
                            height: SPAN,
                            width: 1,
                            background: "var(--w-edge)"
                          }
                        },
                        `v${i}`
                      )),
                      STARS.map(([r, c]) => /* @__PURE__ */ (0, import_jsx_runtime25.jsx)(
                        "span",
                        {
                          "aria-hidden": "true",
                          style: {
                            position: "absolute",
                            left: PAD + c * CELL - 3,
                            top: PAD + r * CELL - 3,
                            width: 6,
                            height: 6,
                            borderRadius: 99,
                            background: "var(--w-ink-muted)"
                          }
                        },
                        `s${r}-${c}`
                      )),
                      Array.from(
                        { length: SIZE },
                        (_, r) => Array.from({ length: SIZE }, (_2, c) => {
                          const stone = stones.find((s) => s.r === r && s.c === c);
                          return /* @__PURE__ */ (0, import_jsx_runtime25.jsx)(
                            "button",
                            {
                              type: "button",
                              "aria-label": `\u7B2C ${r + 1} \u884C\u7B2C ${c + 1} \u5217`,
                              onClick: () => place(r, c),
                              style: {
                                position: "absolute",
                                left: PAD + c * CELL - 13,
                                top: PAD + r * CELL - 13,
                                width: 26,
                                height: 26,
                                padding: 0,
                                border: 0,
                                background: "none",
                                cursor: "pointer",
                                display: "flex",
                                alignItems: "center",
                                justifyContent: "center"
                              },
                              children: stone ? /* @__PURE__ */ (0, import_jsx_runtime25.jsx)(
                                "span",
                                {
                                  style: {
                                    width: CELL * 0.82,
                                    height: CELL * 0.82,
                                    borderRadius: 99,
                                    background: stone.black ? "var(--w-ink-display)" : "var(--w-bg)",
                                    border: stone.black ? "0" : "1px solid var(--w-edge)"
                                  }
                                }
                              ) : null
                            },
                            `p${r}-${c}`
                          );
                        })
                      )
                    ]
                  }
                )
              ]
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime25.jsx)(
            Glass,
            {
              tone: "card",
              radius: 12,
              style: { flex: "1 1 320px", minWidth: 296, margin: "8px 8px 8px 0", order: 1 },
              children: /* @__PURE__ */ (0, import_jsx_runtime25.jsxs)(
                "div",
                {
                  style: {
                    display: "flex",
                    flexDirection: "column",
                    gap: "var(--w-space-2)",
                    padding: 8,
                    minHeight: 420,
                    height: "100%",
                    boxSizing: "border-box"
                  },
                  children: [
                    /* @__PURE__ */ (0, import_jsx_runtime25.jsx)(
                      "div",
                      {
                        style: {
                          display: "flex",
                          flexDirection: "column",
                          gap: "var(--w-space-2)",
                          flex: 1,
                          overflowY: "auto"
                        },
                        children: messages.map((text, index) => {
                          const mine = text.startsWith("\u4F60: ");
                          return /* @__PURE__ */ (0, import_jsx_runtime25.jsx)("div", { style: { display: "flex", justifyContent: mine ? "flex-end" : "flex-start" }, children: /* @__PURE__ */ (0, import_jsx_runtime25.jsx)(
                            "div",
                            {
                              style: {
                                maxWidth: 260,
                                padding: "8px 10px",
                                borderRadius: "var(--w-radius-well)",
                                background: mine ? "var(--w-signal)" : "var(--w-surface-tint)",
                                color: mine ? "var(--w-ink-on-signal)" : "var(--w-ink-body)",
                                border: mine ? "0" : "1px solid var(--w-hairline)",
                                fontSize: "var(--w-size-small)",
                                lineHeight: 1.55,
                                wordBreak: "break-word"
                              },
                              children: text
                            }
                          ) }, index);
                        })
                      }
                    ),
                    /* @__PURE__ */ (0, import_jsx_runtime25.jsxs)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-2)" }, children: [
                      /* @__PURE__ */ (0, import_jsx_runtime25.jsx)(
                        InlineField,
                        {
                          value: input,
                          onChange: setInput,
                          placeholder: "\u548C\u5C0F\u96E8\u8BF4\u70B9\u4EC0\u4E48\u2026",
                          onEnter: send,
                          style: { flex: 1 }
                        }
                      ),
                      /* @__PURE__ */ (0, import_jsx_runtime25.jsx)(GlassButton, { size: "sm", onClick: send, children: "\u53D1\u9001" })
                    ] })
                  ]
                }
              )
            }
          )
        ]
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime25.jsx)(
      "div",
      {
        style: {
          padding: "6px 8px 10px",
          background: "var(--w-tint-bar)",
          borderTop: "1px solid var(--w-hairline)",
          display: "flex",
          flexDirection: "column",
          gap: "var(--w-space-2)"
        },
        children: /* @__PURE__ */ (0, import_jsx_runtime25.jsxs)("div", { style: { display: "flex", flexWrap: "wrap", justifyContent: "center", gap: "var(--w-space-3)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime25.jsx)(GlassButton, { size: "sm", children: "\u5632\u8BBD" }),
          /* @__PURE__ */ (0, import_jsx_runtime25.jsx)(
            "button",
            {
              type: "button",
              className: "k-textbtn",
              style: { color: "var(--w-signal)", height: 34, padding: "0 14px" },
              children: "\u6295\u964D"
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime25.jsx)(GlassButton, { size: "sm", children: "\u6717\u8BFB" }),
          /* @__PURE__ */ (0, import_jsx_runtime25.jsx)(
            "button",
            {
              type: "button",
              className: "k-textbtn",
              style: {
                height: 34,
                width: 44,
                padding: 0,
                background: "var(--w-surface-tint)",
                borderRadius: "var(--w-radius-button)",
                color: "var(--w-ink-body)"
              },
              children: "\u8BED\u97F3"
            }
          )
        ] })
      }
    )
  ] });
}

// src/screens/games/map.tsx
var import_react18 = __toESM(require_react(), 1);
var import_jsx_runtime26 = __toESM(require_jsx_runtime(), 1);
var CHIP = { margin: "0 8px 8px 0" };
var TILE_SOURCES = ["\u624B\u5DE5\u7ED8\u5236", "\u9AD8\u5FB7\u8DEF\u7F51", "\u9AD8\u5FB7\u536B\u661F", "OSM", "\u672C\u5730\u7F13\u5B58", "\u79BB\u7EBF\u5305"];
var TILE_NOTES = {
  \u624B\u5DE5\u7ED8\u5236: "\u53EA\u753B\u4F60\u81EA\u5DF1\u7684\u5730\u70B9\u4E0E\u8DEF\u7F51\uFF0C\u4E0D\u8054\u7F51\u3002",
  \u9AD8\u5FB7\u8DEF\u7F51: "\u9AD8\u5FB7\u74E6\u7247\uFF08GCJ-02\uFF0C\u843D\u56FE\u65F6\u6309 WGS-84 \u504F\u56DE\u6765\uFF09\uFF0C\u4E2D\u6587\u8DEF\u540D\u3002",
  \u9AD8\u5FB7\u536B\u661F: "\u9AD8\u5FB7\u536B\u661F\u5F71\u50CF\uFF08GCJ-02\uFF09\uFF0C\u6CA1\u6709\u8DEF\u540D\uFF0C\u9002\u5408\u770B\u5730\u5757\u4E0E\u5C4B\u9876\u3002",
  OSM: "\u5F00\u653E\u8857\u9053\u56FE\uFF08WGS-84\uFF09\u3002\u5FD7\u613F\u8005\u670D\u52A1\u5668\uFF0C\u5E76\u53D1\u538B\u5230 2\uFF0C\u522B\u5F53\u81EA\u5BB6\u56FE\u5E8A\u7528\u3002",
  \u672C\u5730\u7F13\u5B58: "\u53EA\u7528\u672C\u5730\u7F13\u5B58\u91CC\u7684\u74E6\u7247\uFF0C\u4E0D\u53D1\u4EFB\u4F55\u8BF7\u6C42\u3002\u5207\u5230\u8FD9\u4E2A\u6765\u6E90\u524D\u5148\u8054\u7F51\u901B\u4E00\u904D\u8981\u7528\u7684\u533A\u57DF\u3002",
  \u79BB\u7EBF\u5305: "\u8BFB\u53D6\u4F60\u9009\u7684 .mbtiles \u6587\u4EF6\uFF1A\u6574\u4E2A\u5730\u533A\u90FD\u5728\u672C\u5730\uFF0C\u6CA1\u7F51\u4E5F\u80FD\u770B\u3002"
};
var MODE_LABEL = {
  view: "\u6D4F\u89C8",
  edit: "\u7F16\u8F91",
  link: "\u8FDE\u7EBF",
  doodle: "\u6D82\u9E26"
};
function Map2({ navigate, back }) {
  const [panelOpen, setPanelOpen] = (0, import_react18.useState)(false);
  const [searchOpen, setSearchOpen] = (0, import_react18.useState)(false);
  const [searchText, setSearchText] = (0, import_react18.useState)("");
  const [mode, setMode] = (0, import_react18.useState)("view");
  const [regionEdit, setRegionEdit] = (0, import_react18.useState)(false);
  const [weather, setWeather] = (0, import_react18.useState)("\u8DDF\u968F\u7528\u6237\u5929\u6C14");
  const [tileSource, setTileSource] = (0, import_react18.useState)("\u624B\u5DE5\u7ED8\u5236");
  const [onlineTiles, setOnlineTiles] = (0, import_react18.useState)(true);
  const [curve, setCurve] = (0, import_react18.useState)(false);
  const modeChip = (value) => /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Well3, { active: mode === value, onClick: () => setMode(value), style: CHIP, children: mode === value ? `\u25CF ${MODE_LABEL[value]}` : MODE_LABEL[value] }, value);
  return /* @__PURE__ */ (0, import_jsx_runtime26.jsxs)("div", { style: { position: "relative", height: "100%", minHeight: "100%", overflow: "hidden" }, children: [
    /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(
      "div",
      {
        "aria-label": "\u5730\u56FE\u753B\u5E03",
        style: {
          position: "absolute",
          inset: 0,
          backgroundImage: "linear-gradient(var(--w-hairline) 1px, transparent 1px), linear-gradient(90deg, var(--w-hairline) 1px, transparent 1px)",
          backgroundSize: "24px 24px"
        }
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime26.jsxs)(
      "div",
      {
        style: {
          position: "absolute",
          left: 16,
          top: 14,
          width: 360,
          maxWidth: 420,
          display: "flex",
          flexDirection: "column",
          gap: "var(--w-space-2)",
          zIndex: 4
        },
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime26.jsxs)(
            "div",
            {
              onClick: () => setSearchOpen(true),
              style: {
                display: "flex",
                alignItems: "center",
                gap: 8,
                height: 44,
                cursor: "text",
                padding: "0 12px",
                borderRadius: 99,
                border: "1px solid var(--w-edge)",
                background: "var(--w-surface-tint)"
              },
              children: [
                /* @__PURE__ */ (0, import_jsx_runtime26.jsx)("span", { "aria-hidden": "true", style: { fontSize: "var(--w-size-small)" }, children: "\u{1F50D}" }),
                /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(
                  "input",
                  {
                    type: "text",
                    value: searchText,
                    placeholder: "\u641C\u7D22\u5730\u70B9\u3001\u533A\u57DF\u2026",
                    "aria-label": "\u641C\u7D22\u5730\u70B9\u3001\u533A\u57DF\u2026",
                    onFocus: () => setSearchOpen(true),
                    onChange: (event) => setSearchText(event.target.value),
                    style: {
                      flex: 1,
                      minWidth: 0,
                      border: 0,
                      outline: "none",
                      background: "transparent",
                      color: "var(--w-ink-body)",
                      font: "inherit",
                      fontSize: "var(--w-size-body)"
                    }
                  }
                ),
                searchOpen ? /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(
                  "button",
                  {
                    type: "button",
                    "aria-label": "\u6536\u8D77\u641C\u7D22",
                    onClick: () => setSearchOpen(false),
                    style: {
                      border: 0,
                      background: "none",
                      cursor: "pointer",
                      font: "inherit",
                      fontSize: "var(--w-size-small)",
                      color: "var(--w-ink-muted)"
                    },
                    children: "\u2715"
                  }
                ) : null
              ]
            }
          ),
          searchOpen ? /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(
            "div",
            {
              style: {
                padding: 6,
                borderRadius: "var(--w-radius-button)",
                border: "1px solid var(--w-edge)",
                background: "var(--w-surface-tint)"
              },
              children: searchText.trim().length === 0 ? /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Cap2, { style: { padding: "6px 8px" }, children: "\u641C\u7D22\u5730\u70B9\u3001\u533A\u57DF\uFF0C\u6216\u8BF4\u53E5\u8BDD\u8BA9\u5979\u5E26\u4F60\u53BB" }) : null
            }
          ) : null
        ]
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime26.jsx)("div", { style: { position: "absolute", right: 16, top: 14, zIndex: 7 }, children: /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Fab, { label: "\u2630", size: 46, fontSize: "var(--w-size-subhead)", title: "\u56FE\u5C42", onClick: () => setPanelOpen(!panelOpen) }) }),
    panelOpen ? /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(
      "button",
      {
        type: "button",
        "aria-label": "\u5173\u95ED\u56FE\u5C42\u9762\u677F",
        onClick: () => setPanelOpen(false),
        style: {
          position: "absolute",
          inset: 0,
          border: 0,
          padding: 0,
          background: "var(--w-ink-display)",
          opacity: 0.35,
          cursor: "pointer",
          zIndex: 5
        }
      }
    ) : null,
    panelOpen ? /* @__PURE__ */ (0, import_jsx_runtime26.jsxs)(
      "div",
      {
        style: {
          position: "absolute",
          right: 10,
          top: 10,
          bottom: 10,
          width: 336,
          display: "flex",
          flexDirection: "column",
          gap: 10,
          padding: "14px 16px 16px",
          borderRadius: "var(--w-radius-button)",
          border: "1px solid var(--w-edge)",
          background: "var(--w-surface-tint)",
          overflowY: "auto",
          zIndex: 6
        },
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime26.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Section, { children: "\u56FE\u5C42" }),
            /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(
              "button",
              {
                type: "button",
                "aria-label": "\u5173\u95ED",
                onClick: () => setPanelOpen(false),
                className: "k-textbtn",
                style: { fontSize: "var(--w-size-body)" },
                children: "\u2715"
              }
            )
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Section, { muted: true, children: "\u56FE\u5C42" }),
          /* @__PURE__ */ (0, import_jsx_runtime26.jsxs)(ToolRow, { children: [
            /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(
              Well3,
              {
                active: regionEdit,
                onClick: () => setRegionEdit(!regionEdit),
                style: CHIP,
                children: regionEdit ? "\u25CF \u533A\u57DF\u5212\u5206" : "\u533A\u57DF\u5212\u5206"
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(
              "button",
              {
                type: "button",
                className: "k-textbtn",
                style: {
                  margin: "0 8px 8px 0",
                  padding: "7px 10px",
                  borderRadius: "var(--w-radius-well)",
                  background: "var(--w-surface-tint)",
                  color: "var(--w-ink-body)",
                  fontSize: "var(--w-size-small)"
                },
                onClick: () => setWeather(
                  (current) => current === "\u8DDF\u968F\u7528\u6237\u5929\u6C14" ? "\u5730\u56FE\u5F53\u5730\u5929\u6C14" : current === "\u5730\u56FE\u5F53\u5730\u5929\u6C14" ? "\u81EA\u5B9A\u4E49\u5730\u70B9\u5929\u6C14" : "\u8DDF\u968F\u7528\u6237\u5929\u6C14"
                ),
                children: weather
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(
              "button",
              {
                type: "button",
                className: "k-textbtn",
                style: {
                  margin: "0 8px 8px 0",
                  padding: "7px 10px",
                  borderRadius: "var(--w-radius-well)",
                  background: "var(--w-surface-tint)",
                  color: "var(--w-ink-body)",
                  fontSize: "var(--w-size-small)"
                },
                onClick: () => navigate("/minimal3d"),
                children: "3D \u7070\u5EA6"
              }
            )
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Section, { muted: true, children: "\u5730\u56FE\u6765\u6E90" }),
          /* @__PURE__ */ (0, import_jsx_runtime26.jsx)("div", { style: { display: "flex", flexWrap: "wrap" }, children: TILE_SOURCES.map((name) => /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(
            Well3,
            {
              active: tileSource === name,
              onClick: () => setTileSource(name),
              style: { margin: "0 8px 8px 0" },
              children: name
            },
            name
          )) }),
          /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Cap2, { children: TILE_NOTES[tileSource] }),
          /* @__PURE__ */ (0, import_jsx_runtime26.jsxs)(
            "div",
            {
              style: {
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                gap: 10,
                padding: "2px 0"
              },
              children: [
                /* @__PURE__ */ (0, import_jsx_runtime26.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 1, minWidth: 0 }, children: [
                  /* @__PURE__ */ (0, import_jsx_runtime26.jsx)("span", { style: { fontSize: "var(--w-size-small)", color: "var(--w-ink-body)" }, children: "\u5141\u8BB8\u8054\u7F51\u53D6\u74E6\u7247" }),
                  /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Cap2, { children: "\u951A\u70B9\uFF1A\u4E0A\u6D77\uFF08\u9ED8\u8BA4\u843D\u70B9\uFF09 \u2014\u2014 \u8FD9\u5F20\u56FE\u6CA1\u6709\u7ECF\u7EAC\u5EA6\uFF0C\u5E95\u56FE\u53EA\u5F53\u80CC\u666F\u94FA\uFF0C\u4E0D\u5BF9\u5E94\u771F\u5B9E\u4F4D\u7F6E\u3002" })
                ] }),
                /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(
                  "button",
                  {
                    type: "button",
                    role: "switch",
                    "aria-checked": onlineTiles,
                    "aria-label": "\u5141\u8BB8\u8054\u7F51\u53D6\u74E6\u7247",
                    onClick: () => setOnlineTiles(!onlineTiles),
                    style: {
                      flex: "0 0 auto",
                      width: 46,
                      height: 27,
                      borderRadius: 99,
                      border: "1px solid var(--w-signal-edge)",
                      background: onlineTiles ? "var(--w-signal)" : "var(--w-surface-tint)",
                      cursor: "pointer",
                      position: "relative",
                      padding: 0
                    },
                    children: /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(
                      "span",
                      {
                        style: {
                          position: "absolute",
                          top: 3,
                          left: onlineTiles ? 22 : 3,
                          width: 19,
                          height: 19,
                          borderRadius: 99,
                          background: "var(--w-bg)"
                        }
                      }
                    )
                  }
                )
              ]
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Cap2, { children: tileSource === "\u624B\u5DE5\u7ED8\u5236" ? "\u5E95\u56FE\u5173\u7740\uFF1A\u7F16\u8F91\u5668\u7684\u7EB8\u9762\u753B\u5E03\u3002" : `${tileSource} \xB7 \u8054\u7F51${onlineTiles ? "\u5DF2\u5F00" : "\u5DF2\u5173"}` }),
          /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Section, { muted: true, children: "\u7F16\u8F91" }),
          /* @__PURE__ */ (0, import_jsx_runtime26.jsxs)("div", { style: { display: "flex", flexWrap: "wrap" }, children: [
            modeChip("view"),
            modeChip("edit"),
            modeChip("link"),
            modeChip("doodle"),
            mode === "link" ? /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Well3, { onClick: () => setCurve(!curve), style: CHIP, children: curve ? "\u66F2\u7EBF" : "\u76F4\u7EBF" }) : null,
            [
              "\u21BB \u91CD\u7F6E\u5E03\u5C40",
              "\uFF0B \u65B0\u5730\u70B9",
              "\uFF0B \u65B0\u573A\u666F",
              "\u80CC\u666F\uFF08\u7D20\u6750\u5E93\uFF09",
              "AI \u753B\u80CC\u666F"
            ].map((label) => /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Well3, { style: { margin: "0 8px 8px 0" }, children: label }, label))
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Cap2, { children: "\u7F16\u8F91\uFF1A\u62D6\u5361\u7247=\u79FB\u52A8 \xB7 \u7A7A\u767D\u62D6\u52A8=\u5E73\u79FB \xB7 \u6309\u4F4F\u7A7A\u767D 0.6 \u79D2\u518D\u62D6=\u6846\u9009 \xB7 \u53F3\u952E=\u83DC\u5355" }),
          /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Section, { muted: true, children: "\u5BFC\u5165\u5BFC\u51FA" }),
          /* @__PURE__ */ (0, import_jsx_runtime26.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: 8 }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(
              "span",
              {
                style: {
                  fontSize: "var(--w-size-caption)",
                  color: "var(--w-ink-body)",
                  whiteSpace: "nowrap",
                  overflow: "hidden",
                  textOverflow: "ellipsis"
                },
                children: "\u8FD9\u5F20\u56FE\u6765\u81EA\uFF1A\u624B\u5DE5\u7ED8\u5236"
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(
              "button",
              {
                type: "button",
                className: "k-textbtn",
                style: {
                  flex: "0 0 auto",
                  height: 30,
                  padding: "0 10px",
                  borderRadius: "var(--w-radius-button)",
                  border: "1px solid var(--w-hairline)",
                  fontSize: "var(--w-size-caption)",
                  color: "var(--w-ink-body)"
                },
                onClick: () => navigate("/mapcreate"),
                children: "\u6362\u6765\u6E90\u2026"
              }
            )
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime26.jsxs)("div", { style: { display: "flex", flexWrap: "wrap" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Well3, { onClick: () => void 0, style: CHIP, children: "\u5BFC\u5165\u5730\u56FE" }),
            /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Well3, { onClick: () => navigate("/mapcapture"), style: CHIP, children: "\u53D6\u666F\uFF08\u62CD\u7167\u9489\u5728\u5730\u56FE\u4E0A\uFF09" }),
            /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Well3, { onClick: () => navigate("/mapcreate"), style: CHIP, children: "\u521B\u5EFA\u5730\u56FE\u2026\uFF08\u9AD8\u5FB7 / \u5F00\u653E\u8857\u9053\u56FE\uFF09" }),
            /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Well3, { style: CHIP, children: "\u5BFC\u51FA" }),
            /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Well3, { onClick: () => navigate("/flows"), style: CHIP, children: "\u6D41\u7A0B\u56FE" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Section, { muted: true, children: "\u5176\u4ED6" }),
          /* @__PURE__ */ (0, import_jsx_runtime26.jsxs)(ToolRow, { children: [
            /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Well3, { onClick: back, style: CHIP, children: "\u8FD4\u56DE" }),
            /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Well3, { style: CHIP, children: "\u2139 \u5E2E\u52A9" })
          ] })
        ]
      }
    ) : null,
    /* @__PURE__ */ (0, import_jsx_runtime26.jsxs)(
      "div",
      {
        style: {
          position: "absolute",
          right: 16,
          bottom: 118,
          display: "flex",
          flexDirection: "column",
          gap: 10,
          zIndex: 4
        },
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Fab, { label: "\uFF0B", fontSize: "var(--w-size-title)", title: "\u653E\u5927" }),
          /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Fab, { label: "\uFF0D", fontSize: "var(--w-size-title)", title: "\u7F29\u5C0F" }),
          /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Fab, { label: "\u2316", title: "\u56DE\u5230\u5979\u6B64\u523B\u6240\u5728\u7684\u4F4D\u7F6E" }),
          /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Fab, { label: "N", fontSize: "var(--w-size-small)", title: "\u56DE\u6B63\uFF1A\u6B63\u5317\u671D\u4E0A + \u5168\u56FE\u53D6\u666F" })
        ]
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime26.jsxs)(
      "div",
      {
        style: {
          position: "absolute",
          left: 18,
          bottom: 26,
          display: "flex",
          alignItems: "flex-end",
          gap: 6,
          zIndex: 4
        },
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime26.jsx)("span", { style: { width: 60, height: 3, borderRadius: 1, background: "var(--w-ink-body)" } }),
          /* @__PURE__ */ (0, import_jsx_runtime26.jsx)(Cap2, { children: "100 \u7C73" })
        ]
      }
    )
  ] });
}

// src/screens/games/mapcapture.tsx
var import_react20 = __toESM(require_react(), 1);

// src/components/GlassSlider.tsx
var import_react19 = __toESM(require_react(), 1);
var import_jsx_runtime27 = __toESM(require_jsx_runtime(), 1);
var THUMB = 28;
var pct = (v) => `${Math.round(v * 100)}%`;
function GlassSlider({ label, value, onChange, width = 120, disabled, format = pct }) {
  const press = usePress();
  const trackRef = (0, import_react19.useRef)(null);
  const dragging = (0, import_react19.useRef)(false);
  const clamp3 = (v) => Math.min(1, Math.max(0, v));
  const setFromX = (clientX) => {
    const el = trackRef.current;
    if (!el) return;
    const rect = el.getBoundingClientRect();
    const travel = Math.max(1, rect.width - THUMB);
    onChange(clamp3((clientX - rect.left - THUMB / 2) / travel));
  };
  const onPointerDown = (event) => {
    if (disabled) return;
    event.currentTarget.setPointerCapture(event.pointerId);
    dragging.current = true;
    press.down();
    setFromX(event.clientX);
  };
  const onPointerMove = (event) => {
    if (dragging.current) setFromX(event.clientX);
  };
  const endDrag = (event) => {
    dragging.current = false;
    press.up();
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
  };
  const onKeyDown = (event) => {
    if (disabled) return;
    const step = event.shiftKey ? 0.1 : 0.02;
    const map = {
      ArrowLeft: value - step,
      ArrowDown: value - step,
      ArrowRight: value + step,
      ArrowUp: value + step,
      PageDown: value - 0.1,
      PageUp: value + 0.1,
      Home: 0,
      End: 1
    };
    const next = map[event.key];
    if (next === void 0) return;
    event.preventDefault();
    press.flash();
    onChange(clamp3(next));
  };
  return /* @__PURE__ */ (0, import_jsx_runtime27.jsxs)(
    "div",
    {
      ref: (node) => {
        press.ref.current = node;
        trackRef.current = node;
      },
      className: "slider",
      role: "slider",
      tabIndex: disabled ? -1 : 0,
      "aria-label": label,
      "aria-disabled": disabled || void 0,
      "aria-valuemin": 0,
      "aria-valuemax": 100,
      "aria-valuenow": Math.round(value * 100),
      "aria-valuetext": format(value),
      "data-disabled": disabled ? "1" : void 0,
      style: { "--p": value, width: `${width}px` },
      onPointerDown,
      onPointerMove,
      onPointerUp: endDrag,
      onPointerCancel: endDrag,
      onKeyDown,
      children: [
        /* @__PURE__ */ (0, import_jsx_runtime27.jsx)(Glass, { tone: "track", radius: 6, className: "slider__track" }),
        /* @__PURE__ */ (0, import_jsx_runtime27.jsx)("span", { className: "slider__fill", "aria-hidden": "true" }),
        /* @__PURE__ */ (0, import_jsx_runtime27.jsx)("span", { className: "slider__bump", "aria-hidden": "true" }),
        /* @__PURE__ */ (0, import_jsx_runtime27.jsx)("span", { className: "slider__tension", "aria-hidden": "true" }),
        /* @__PURE__ */ (0, import_jsx_runtime27.jsx)(Glass, { tone: "thumb", radius: 14, className: "slider__thumb" })
      ]
    }
  );
}

// src/screens/games/mapcapture.tsx
var import_jsx_runtime28 = __toESM(require_jsx_runtime(), 1);
var STREETS = [{ value: "\u4E2D\u5C71\u8DEF", label: "\u4E2D\u5C71\u8DEF" }];
function MapCapture({ back }) {
  const [street, setStreet] = (0, import_react20.useState)(true);
  const [streetPick, setStreetPick] = (0, import_react20.useState)("\u4E2D\u5C71\u8DEF");
  const [streetT, setStreetT] = (0, import_react20.useState)(0.5);
  return /* @__PURE__ */ (0, import_jsx_runtime28.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime28.jsx)(PageHeader, { title: "\u53D6\u666F\u4E0E\u5BFC\u5165", subtitle: "\u8FD8\u6CA1\u51FA\u53D1", onBack: back }),
    /* @__PURE__ */ (0, import_jsx_runtime28.jsxs)("div", { className: "screen", style: { maxWidth: 720, gap: "var(--w-space-3)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime28.jsx)(Glass, { tone: "card", radius: 12, children: /* @__PURE__ */ (0, import_jsx_runtime28.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)", padding: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime28.jsx)(Section, { children: "\u7167\u7247\u8FDB\u51FA" }),
        /* @__PURE__ */ (0, import_jsx_runtime28.jsx)(Cap2, { children: "\u7535\u8111\u7AEF\u4E0D\u80FD\u53D6\u666F\uFF1A\u7167\u7247\u4F20\u8FC7\u6765\u5728\u8FD9\u91CC\u5BFC\u5165\u3002\u56FE\u4E0A\u4F1A\u5148\u843D\u6210\u300C\u672A\u7ED1\u300D\uFF0C\u9009\u4E2D\u4E00\u5F20\u518D\u8BB0\u7ED1\u5B9A\u3002" }),
        /* @__PURE__ */ (0, import_jsx_runtime28.jsxs)("div", { style: { display: "flex", flexWrap: "wrap", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime28.jsx)(GlassButton, { size: "sm", children: "\u5BFC\u5165\u7167\u7247\u2026" }),
          /* @__PURE__ */ (0, import_jsx_runtime28.jsx)(GlassButton, { size: "sm", children: "\u6253\u5F00\u53D6\u666F\u5305\u2026" }),
          /* @__PURE__ */ (0, import_jsx_runtime28.jsx)(GlassButton, { size: "sm", children: "\u5BFC\u51FA\u53D6\u666F\u5305\u2026" })
        ] })
      ] }) }),
      /* @__PURE__ */ (0, import_jsx_runtime28.jsx)(Glass, { tone: "card", radius: 12, children: /* @__PURE__ */ (0, import_jsx_runtime28.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)", padding: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime28.jsxs)("div", { style: { display: "flex", flexWrap: "wrap", alignItems: "center", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime28.jsx)(Section, { children: "\u8FD9\u4E00\u7EC4\u7ED1\u5B9A" }),
          /* @__PURE__ */ (0, import_jsx_runtime28.jsx)(
            Well3,
            {
              active: street,
              onClick: () => setStreet(true),
              style: { padding: "4px 12px", border: "1px solid var(--w-hairline)" },
              children: "\u8857\u9053"
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime28.jsx)(
            Well3,
            {
              active: !street,
              onClick: () => setStreet(false),
              style: { padding: "4px 12px", border: "1px solid var(--w-hairline)" },
              children: "\u5BA4\u5185"
            }
          )
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime28.jsx)(Cap2, { children: "\u8FD9\u7EC4\u7ED1\u5B9A\u7ED9\u8C01\uFF1A\u65B0\u62CD\u7684\u90A3\u5F20" }),
        /* @__PURE__ */ (0, import_jsx_runtime28.jsx)("div", { children: /* @__PURE__ */ (0, import_jsx_runtime28.jsx)(GlassButton, { size: "md", children: "\u8BB0\u4E0A\u8FD9\u7EC4\u7ED1\u5B9A" }) }),
        street ? /* @__PURE__ */ (0, import_jsx_runtime28.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime28.jsx)(Cap2, { children: "\u8857\u9053\u7167\u843D\u5728\u8FD9\u6761\u8857\u4E0A\uFF1A\u9009\u8857 \u2192 \u62C9\u4F4D\u7F6E\u3002\u540C\u4E00\u8857\u9053\u4E0D\u540C\u4F4D\u7F6E\u53EF\u5404\u62CD\u4E00\u5F20\u3002" }),
          /* @__PURE__ */ (0, import_jsx_runtime28.jsx)(Select, { value: streetPick, options: STREETS, onChange: setStreetPick, label: "\u9009\u4E00\u6761\u8857" }),
          /* @__PURE__ */ (0, import_jsx_runtime28.jsxs)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-2)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime28.jsx)("div", { style: { flex: 1 }, children: /* @__PURE__ */ (0, import_jsx_runtime28.jsx)(
              GlassSlider,
              {
                label: "\u4F4D\u7F6E",
                value: streetT,
                onChange: setStreetT,
                width: 260,
                format: (value) => `\u4F4D\u7F6E ${Math.round(value * 100)}%`
              }
            ) }),
            /* @__PURE__ */ (0, import_jsx_runtime28.jsxs)(Cap2, { children: [
              "\u4F4D\u7F6E ",
              Math.round(streetT * 100),
              "%"
            ] })
          ] })
        ] }) : /* @__PURE__ */ (0, import_jsx_runtime28.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime28.jsx)(Cap2, { children: "\u5BA4\u5185\u7167\u5FC5\u987B\u843D\u5728\u4E00\u5C42\u4E00\u6237\u4E0A\uFF1A\u697C\u680B \u2192 \u697C\u5C42 \u2192 \u6237\u578B\uFF08+ \u95E8/\u7279\u6B8A\u5730\u70B9\uFF09\u3002" }),
          /* @__PURE__ */ (0, import_jsx_runtime28.jsx)("span", { children: /* @__PURE__ */ (0, import_jsx_runtime28.jsx)(Select, { value: "", options: [{ value: "", label: "\u9009\u697C\u680B" }], onChange: () => void 0, label: "\u9009\u697C\u680B" }) }),
          /* @__PURE__ */ (0, import_jsx_runtime28.jsx)("span", { children: /* @__PURE__ */ (0, import_jsx_runtime28.jsx)(Select, { value: "", options: [{ value: "", label: "\u9009\u697C\u5C42" }], onChange: () => void 0, label: "\u9009\u697C\u5C42" }) }),
          /* @__PURE__ */ (0, import_jsx_runtime28.jsx)("span", { children: /* @__PURE__ */ (0, import_jsx_runtime28.jsx)(Select, { value: "", options: [{ value: "", label: "\u9009\u6237\u578B\uFF08\u573A\u666F\uFF09" }], onChange: () => void 0, label: "\u9009\u6237\u578B\uFF08\u573A\u666F\uFF09" }) }),
          /* @__PURE__ */ (0, import_jsx_runtime28.jsx)("span", { children: /* @__PURE__ */ (0, import_jsx_runtime28.jsx)(
            Select,
            {
              value: "",
              options: [{ value: "", label: "\u95E8 / \u7279\u6B8A\u5730\u70B9\uFF08\u53EF\u4E0D\u9009\uFF09" }],
              onChange: () => void 0,
              label: "\u95E8 / \u7279\u6B8A\u5730\u70B9\uFF08\u53EF\u4E0D\u9009\uFF09"
            }
          ) })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime28.jsx)(Cap2, { children: "\u7535\u8111\u7AEF\u4E0D\u53D6\u666F\uFF1A\u7167\u7247\u5148\u300C\u5BFC\u5165\u300D\uFF0C\u518D\u56DE\u6765\u8FD9\u91CC\u62E3\u8857/\u62E3\u6237\u578B\u8865\u7ED1\u3002" })
      ] }) }),
      /* @__PURE__ */ (0, import_jsx_runtime28.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-1)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime28.jsx)(Section, { children: "\u5DF2\u6302\u4E0A\u7684\u7167\u7247" }),
        /* @__PURE__ */ (0, import_jsx_runtime28.jsx)(Cap2, { children: "\u8FD8\u6CA1\u6709\u7167\u7247\u3002\u624B\u673A\u7AEF\u62CD\uFF0C\u6216\u4E0A\u9762\u300C\u5BFC\u5165\u7167\u7247\u300D\u3002" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime28.jsx)("div", { style: { height: 1, background: "var(--w-hairline)" } })
    ] })
  ] });
}

// src/screens/games/mapcreate.tsx
var import_react21 = __toESM(require_react(), 1);
var import_jsx_runtime29 = __toESM(require_jsx_runtime(), 1);
var PRESETS = [
  { label: "\u8857\u533A", radius: 200 },
  { label: "\u4E00\u516C\u91CC", radius: 500 },
  { label: "\u4E94\u516C\u91CC", radius: 2500 }
];
var BATCHES = [
  { value: "3 \u5F20", label: "3 \u5F20" },
  { value: "6 \u5F20", label: "6 \u5F20" },
  { value: "12 \u5F20", label: "12 \u5F20" }
];
var WEATHER_CHOICES = [
  { value: "user", label: "\u8DDF\u968F\u7528\u6237\u5929\u6C14" },
  { value: "map", label: "\u5730\u56FE\u5F53\u5730\u5929\u6C14" },
  { value: "custom", label: "\u81EA\u5B9A\u4E49\u5730\u70B9\u5929\u6C14" }
];
function radiusLabel(radius) {
  const span = radius * 2;
  const text = span >= 1e3 ? `${(span / 1e3).toFixed(2).replace(/\.?0+$/, "")} \u516C\u91CC\u5BBD\u7684\u4E00\u6574\u7247` : `${Math.round(radius)} \u7C73\u4E3A\u534A\u5F84\u7684\u4E00\u5C0F\u7247`;
  return `\u53D6\u4E2D\u5FC3\u70B9\u5468\u56F4 ${text}`;
}
function MapCreate({ back }) {
  const [query, setQuery] = (0, import_react21.useState)("");
  const [radius, setRadius] = (0, import_react21.useState)(500);
  const [weather, setWeather] = (0, import_react21.useState)("user");
  const [batch, setBatch] = (0, import_react21.useState)("3 \u5F20");
  const [amapKey, setAmapKey] = (0, import_react21.useState)("");
  const [googleKey, setGoogleKey] = (0, import_react21.useState)("");
  const [city, setCity] = (0, import_react21.useState)("");
  const [signalAmap, setSignalAmap] = (0, import_react21.useState)(false);
  return /* @__PURE__ */ (0, import_jsx_runtime29.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(PageHeader, { title: "\u521B\u5EFA\u5730\u56FE", subtitle: "\u8F93\u5165\u57CE\u5E02\u3001\u533A\u53BF\u3001\u8857\u9053\uFF0C\u6216\u8005\u76F4\u63A5\u586B\u7ECF\u7EAC\u5EA6\uFF08\u4F8B\uFF1A120.15,30.27\uFF09\u3002", onBack: back }),
    /* @__PURE__ */ (0, import_jsx_runtime29.jsxs)("div", { className: "screen", style: { maxWidth: 920, gap: "var(--w-space-4)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Section, { children: "\u2460 \u627E\u5730\u65B9" }),
      /* @__PURE__ */ (0, import_jsx_runtime29.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime29.jsxs)("div", { style: { display: "flex", gap: "var(--w-space-2)", alignItems: "center" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(
            InlineField,
            {
              value: query,
              onChange: setQuery,
              placeholder: "\u57CE\u5E02 / \u533A\u53BF / \u8857\u9053\uFF0C\u6216\u76F4\u63A5\u5199\u7ECF\u7EAC\u5EA6\uFF1A120.15,30.27",
              fontSize: "var(--w-size-small)",
              style: { flex: 1 }
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(GlassButton, { size: "md", children: "\u68C0\u7D22" })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Cap2, { children: "\u8FD8\u6CA1\u586B\u9AD8\u5FB7 Key\uFF0C\u68C0\u7D22\u9000\u56DE\u5F00\u653E\u8857\u9053\u56FE \u2014\u2014 \u56FD\u5185\u7684\u4E2D\u6587\u5E97\u540D\u4E0E\u5C0F\u8DEF\u4F1A\u5C11\u4E00\u4E9B\u3002\u586B\u4E0A Key \u81EA\u52A8\u6539\u7528\u9AD8\u5FB7\u3002" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime29.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Section, { children: "\u2461 \u53D6\u591A\u5927\u4E00\u7247" }),
        /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(ToolRow, { children: PRESETS.map((preset) => /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(
          Well3,
          {
            active: radius === preset.radius,
            onClick: () => setRadius(preset.radius),
            style: { padding: "6px 12px", borderRadius: 99 },
            children: preset.label
          },
          preset.label
        )) }),
        /* @__PURE__ */ (0, import_jsx_runtime29.jsxs)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Cap2, { children: "\u534A\u5F84" }),
          /* @__PURE__ */ (0, import_jsx_runtime29.jsx)("div", { style: { flex: 1 }, children: /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(
            GlassSlider,
            {
              label: "\u534A\u5F84",
              value: (radius - 100) / 2900,
              onChange: (next) => setRadius(Math.round((100 + next * 2900) / 10) * 10),
              width: 280,
              format: (value) => `${Math.round(100 + value * 2900)} \u7C73`
            }
          ) }),
          /* @__PURE__ */ (0, import_jsx_runtime29.jsxs)(Cap2, { style: { minWidth: 56, textAlign: "right" }, children: [
            Math.round(radius),
            " \u7C73"
          ] })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Cap2, { children: radiusLabel(radius) }),
        /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Cap2, { children: "\u4E00\u6B21\u6700\u591A 6\xD76 \u516C\u91CC\uFF0C\u9053\u8DEF 1200 \u6BB5 / \u5EFA\u7B51 600 \u680B / \u5E26\u540D\u5B57\u7684\u5E97\u94FA 300 \u5BB6\u5C01\u9876\uFF1A\u585E\u5F97\u592A\u6EE1\uFF0C\u5B89\u5353\u7AEF\u7684\u753B\u5E03\u4E0E\u5BFB\u8DEF\u4F1A\u5148\u6491\u4E0D\u4F4F\uFF0C\u8D85\u4E86\u4F1A\u780D\u5E76\u8BF4\u660E\u3002" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime29.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Section, { children: "\u2462 \u9884\u89C8\u4E0E\u5BFC\u5165" }),
        /* @__PURE__ */ (0, import_jsx_runtime29.jsxs)("div", { style: { display: "flex", flexWrap: "wrap", alignItems: "center", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(GlassButton, { size: "md", children: "\u9884\u89C8\uFF1A\u5148\u6570\u4E00\u6570" }),
          /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Cap2, { children: "\u9884\u89C8\u4E0D\u5199\u8FDB\u5B58\u6863\uFF0C\u53EA\u7B97\u4E00\u6B21\u7ED9\u4F60\u770B\u62FF\u5230\u591A\u5C11\u4E1C\u897F" }),
          /* @__PURE__ */ (0, import_jsx_runtime29.jsx)("span", { style: { flex: 1 } }),
          /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(GlassButton, { size: "md", children: "\u5BFC\u5165\u5230\u6E38\u620F" })
        ] })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime29.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Section, { children: "\u2463 \u80CC\u666F\uFF1A\u5F62\u72B6\u4E4B\u5916\u7684\u90A3\u4E00\u5C42" }),
        /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Cap2, { tone: "body", children: "\u624B\u4E0A\u8FD8\u6CA1\u6709\u5730\u56FE\uFF1A\u5148\u68C0\u7D22\u4E00\u7247\u771F\u5B9E\u57CE\u5E02\u5BFC\u8FDB\u6765\uFF0C\u80CC\u666F\u624D\u6709\u7740\u843D\u3002" }),
        /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Cap2, { children: "\u771F\u5B9E\u57CE\u5E02\u5BFC\u8FDB\u6765\u53EA\u6709\u5EFA\u7B51\u8F6E\u5ED3\u3001\u9053\u8DEF\u547D\u540D\u4E0E\u5E97\u540D\uFF0C\u753B\u9762\u662F\u7A7A\u7684\uFF1A\u81EA\u5DF1\u62CD\u7684\u7167\u7247\u56DE\u5230\u5730\u56FE\u9875\u53F3\u952E\u573A\u666F \u2192\u300C\u80CC\u666F\u56FE\uFF08\u7535\u8111\u5BFC\u5165\uFF09\u300D\u6302\u4E0A\uFF0C\u8FD9\u91CC\u53EA\u8D1F\u8D23\u8BA9 AI \u8865\u3002" }),
        /* @__PURE__ */ (0, import_jsx_runtime29.jsxs)(ToolRow, { children: [
          /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Select, { value: batch, options: BATCHES, onChange: setBatch, label: "\u4E00\u6B21\u51E0\u5F20" }),
          /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(GlassButton, { size: "sm", children: "\u8BA9 AI \u8865\u80CC\u666F" }),
          /* @__PURE__ */ (0, import_jsx_runtime29.jsx)("span", { style: { flex: 1 } }),
          /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(GlassButton, { size: "sm", children: "\u751F\u6210\u5927\u5730\u56FE\u5E95\u56FE" })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Cap2, { children: "\u51FA\u56FE\u8D70\u300C\u8BBE\u7F6E \u2192 AI \u2192 \u753B\u9762\u751F\u6210\u300D\u90A3\u4E00\u5957\uFF1B\u8FD9\u91CC\u53EA\u653E AI \u73B0\u751F\u6210\u7684\u56FE\u4E0E\u4F60\u81EA\u5DF1\u5BFC\u5165\u7684\u6587\u4EF6\uFF0C\u4E0D\u7528\u4EFB\u4F55\u6E38\u620F\u81EA\u5E26\u6216\u53D7\u7248\u6743\u4FDD\u62A4\u7684\u7D20\u6750\u3002" }),
        /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Glass, { tone: "card", radius: 12, children: /* @__PURE__ */ (0, import_jsx_runtime29.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)", padding: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime29.jsx)("span", { style: { fontSize: "var(--w-size-body)", color: "var(--w-ink-body)" }, children: "\u8C37\u6B4C\u5730\u56FE\u5730\u70B9\u7167\u7247" }),
          /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Cap2, { children: "\u8FD8\u6CA1\u586B Key\uFF1A\u9AD8\u5FB7\u6CA1\u6709\u5730\u70B9\u7167\u7247\u63A5\u53E3\uFF0C\u8C37\u6B4C Places \u6709 \u2014\u2014 \u586B\u4E00\u4E2A\u542F\u7528\u4E86 Places API \u7684 Google Maps API Key \u624D\u80FD\u53D6\u7167\u7247\uFF08\u60F3\u8BA9\u6CA1\u7167\u7247\u7684\u5730\u70B9\u9000\u5230\u8857\u666F\uFF0C\u8FD8\u8981\u542F\u7528 Street View Static API\uFF09\u3002" }),
          /* @__PURE__ */ (0, import_jsx_runtime29.jsxs)("div", { style: { display: "flex", gap: "var(--w-space-2)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(
              InlineField,
              {
                value: googleKey,
                onChange: setGoogleKey,
                placeholder: "Google Maps API Key\uFF08\u9700\u542F\u7528 Places API\uFF09",
                style: { flex: 1 }
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(GlassButton, { size: "sm", children: "\u5B58 Key" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime29.jsxs)(ToolRow, { children: [
            /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Select, { value: batch, options: BATCHES, onChange: setBatch, label: "\u4E00\u6B21\u51E0\u5F20" }),
            /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(GlassButton, { size: "sm", children: "\u4ECE\u8C37\u6B4C\u5730\u56FE\u53D6\u7167\u7247" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Cap2, { children: "\u9AD8\u5FB7\u6CA1\u6709\u5730\u70B9\u7167\u7247\u63A5\u53E3\uFF0C\u8C37\u6B4C Places \u6709\uFF1A\u6309\u5E97\u540D / \u95E8\u724C\u641C\u5230\u5730\u70B9\u540E\u53D6\u5B83\u7684\u7167\u7247\uFF0C\u6CA1\u6709\u7167\u7247\u7684\u5730\u70B9\u56DE\u9000\u8857\u666F\uFF08\u8857\u666F\u8FD8\u8981\u53E6\u542F\u7528 Street View Static API\uFF09\u3002\u4E0B\u8F7D\u7684\u7167\u7247\u53EA\u843D\u5728\u672C\u673A\u5730\u56FE\u76EE\u5F55\u3002" })
        ] }) })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime29.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Section, { children: "\u2464 \u5730\u56FE\u5929\u6C14\uFF1A\u8FD9\u5F20\u56FE\u6309\u54EA\u513F\u7B97" }),
        /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Cap2, { children: "\u5BFC\u5165\u7684\u771F\u5B9E\u57CE\u5E02\u5730\u56FE\u9ED8\u8BA4\u6309\u6293\u53D6\u4F4D\u7F6E\u7684\u5F53\u5730\u5929\u6C14\u7B97\uFF1B\u4E5F\u53EF\u4EE5\u6539\u6210\u8DDF\u968F\u7528\u6237\u8BBE\u7F6E\u91CC\u7684\u57CE\u5E02\uFF0C\u6216\u8005\u81EA\u5DF1\u586B\u4E00\u4E2A\u5730\u70B9\u3002" }),
        /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Glass, { tone: "card", radius: 12, children: /* @__PURE__ */ (0, import_jsx_runtime29.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)", padding: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(
            FormRow,
            {
              title: "\u5929\u6C14\u6309\u54EA\u513F\u7B97",
              right: /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Select, { value: weather, options: WEATHER_CHOICES, onChange: setWeather, label: "\u5929\u6C14\u6309\u54EA\u513F\u7B97" })
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Cap2, { children: "\u8FD9\u5F20\u56FE\uFF1A\u624B\u5DE5\u7ED8\u5236 / \u81EA\u5DF1\u5BFC\u5165 \xB7 \u6CA1\u6709\u57CE\u5E02\u540D \xB7 \u6CA1\u6709\u5750\u6807" }),
          /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Cap2, { children: "\u5F53\u524D\uFF1A\u8DDF\u968F\u7528\u6237\u5929\u6C14\uFF08\u8BBE\u7F6E \u2192 \u5929\u6C14\u4E0E\u751F\u6D3B \u91CC\u9009\u7684\u57CE\u5E02\uFF09\u3002" })
        ] }) }),
        weather === "custom" ? /* @__PURE__ */ (0, import_jsx_runtime29.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Cap2, { children: "\u81EA\u5B9A\u4E49\u5730\u70B9" }),
          /* @__PURE__ */ (0, import_jsx_runtime29.jsxs)("div", { style: { display: "flex", gap: "var(--w-space-2)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(InlineField, { value: city, onChange: setCity, placeholder: "\u57CE\u5E02\u540D\uFF0C\u4F8B\u5982\uFF1A\u676D\u5DDE\u5E02", style: { flex: 1 } }),
            /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(GlassButton, { size: "sm", children: "\u4FDD\u5B58\u5730\u70B9" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Cap2, { children: "\u586B\u57CE\u5E02\u540D\u5C31\u591F\uFF1A\u7B2C\u4E00\u6B21\u67E5\u5929\u6C14\u65F6\u4F1A\u81EA\u52A8\u5730\u7406\u7F16\u7801\u51FA\u5750\u6807\u3002" })
        ] }) : null,
        /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Cap2, { children: "\u4E3B\u754C\u9762\u9876\u90E8\u7684\u5929\u6C14\u3001\u573A\u666F\u7684\u5929\u6C14\u95E8\u63A7\u8BFB\u7684\u90FD\u662F\u8FD9\u4E00\u4EFD\uFF1B\u5730\u56FE\u4E00\u6362\uFF08\u91CD\u65B0\u5BFC\u5165\uFF09\uFF0C\u8FD9\u91CC\u4F1A\u8DDF\u7740\u65B0\u56FE\u91CD\u8BFB\u3002" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime29.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Section, { children: "\u2465 \u81EA\u5DF1\u7684\u6587\u4EF6 / \u9AD8\u5FB7 Key / \u5224\u706F" }),
        /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(
          FormRow,
          {
            title: "\u5BFC\u5165\u6211\u81EA\u5DF1\u51C6\u5907\u597D\u7684\u5730\u56FE",
            desc: "\u4E00\u4EFD\u5730\u56FE JSON\uFF1A\u53EA\u6709\u5F62\u72B6\u4E0E\u8DEF\u540D\uFF0C\u80CC\u666F\u540C\u6837\u8981\u5B9E\u5730\u53D6\u6750\u6216\u8BA9 AI \u8865\u3002",
            right: /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(GlassButton, { size: "sm", children: "\u9009\u6587\u4EF6" })
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime29.jsx)("div", { className: "k-sep", role: "presentation" }),
        /* @__PURE__ */ (0, import_jsx_runtime29.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime29.jsx)("span", { style: { fontSize: "var(--w-size-body)", color: "var(--w-ink-body)" }, children: "\u9AD8\u5FB7 Key\uFF08\u9009\u586B\uFF09" }),
          /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Cap2, { children: "\u6CA1\u586B Key \u4E5F\u80FD\u7528\uFF1A\u68C0\u7D22\u9000\u56DE\u5F00\u653E\u8857\u9053\u56FE\uFF0C\u53EA\u662F\u56FD\u5185\u7684\u4E2D\u6587\u5E97\u540D\u4E0E\u5C0F\u9A6C\u8DEF\u4F1A\u5C11\u4E00\u4E9B\u3002" }),
          /* @__PURE__ */ (0, import_jsx_runtime29.jsxs)("div", { style: { display: "flex", gap: "var(--w-space-2)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(InlineField, { value: amapKey, onChange: setAmapKey, placeholder: "\u9AD8\u5FB7 Web \u670D\u52A1\u7684 key", style: { flex: 1 } }),
            /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(GlassButton, { size: "sm", children: "\u5B58 Key" })
          ] })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime29.jsx)("div", { className: "k-sep", role: "presentation" }),
        /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(
          FormRow,
          {
            title: "\u7EA2\u7EFF\u706F\u770B\u9AD8\u5FB7\u8DEF\u51B5",
            desc: "\u5173\u7740\uFF08\u9ED8\u8BA4\uFF09\uFF1A\u706F\u8272\u53EA\u6309\u6BCF\u76CF\u706F\u81EA\u5DF1\u7684\u5468\u671F\u8D70\uFF0C\u4E0D\u8054\u7F51\u3002\u6D41\u7A0B\u56FE\u91CC\u5224\u300C\u7EFF\u706F\u80FD\u4E0D\u80FD\u8D70\u300D\u7528\u7684\u5C31\u662F\u5B83\u3002",
            right: /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(
              "button",
              {
                type: "button",
                role: "switch",
                "aria-checked": signalAmap,
                "aria-label": "\u7EA2\u7EFF\u706F\u770B\u9AD8\u5FB7\u8DEF\u51B5",
                onClick: () => setSignalAmap(!signalAmap),
                style: {
                  width: 46,
                  height: 27,
                  borderRadius: 99,
                  border: "1px solid var(--w-signal-edge)",
                  background: signalAmap ? "var(--w-signal)" : "var(--w-surface-tint)",
                  cursor: "pointer",
                  position: "relative",
                  padding: 0
                },
                children: /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(
                  "span",
                  {
                    style: {
                      position: "absolute",
                      top: 3,
                      left: signalAmap ? 22 : 3,
                      width: 19,
                      height: 19,
                      borderRadius: 99,
                      background: "var(--w-bg)"
                    }
                  }
                )
              }
            )
          }
        )
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime29.jsx)(Cap2, { children: "\u5BFC\u5165\u7684\u5730\u56FE\u4F1A\u6574\u5F20\u6362\u6389\u624B\u4E0A\u8FD9\u4E00\u5F20\uFF1B\u60F3\u56DE\u53BB\u63A5\u7740\u6539\uFF0C\u56DE\u5730\u56FE\u9875\u5373\u53EF\u3002" })
    ] })
  ] });
}

// src/screens/games/minimal3d.tsx
var import_react22 = __toESM(require_react(), 1);
var import_jsx_runtime30 = __toESM(require_jsx_runtime(), 1);
var GRAY = {
  face: "color-mix(in oklab, var(--w-ink-display) 22%, var(--w-surface-tint))",
  side: "color-mix(in oklab, var(--w-ink-display) 34%, var(--w-surface-tint))",
  floor: "color-mix(in oklab, var(--w-ink-display) 8%, var(--w-surface-tint))",
  sky: "color-mix(in oklab, var(--w-ink-display) 2%, var(--w-surface-tint))",
  line: "color-mix(in oklab, var(--w-ink-display) 16%, transparent)"
};
var BUILDINGS = [
  { x: -290, z: -180, w: 150, h: 170 },
  { x: 300, z: -120, w: 170, h: 210 },
  { x: -330, z: -420, w: 190, h: 260 },
  { x: 340, z: -380, w: 160, h: 150 },
  { x: -320, z: -680, w: 210, h: 320 },
  { x: 330, z: -640, w: 200, h: 240 }
];
function headingOf(deg) {
  if (deg < 22.5 || deg >= 337.5) return "\u5317";
  if (deg < 67.5) return "\u4E1C\u5317";
  if (deg < 112.5) return "\u4E1C";
  if (deg < 157.5) return "\u4E1C\u5357";
  if (deg < 202.5) return "\u5357";
  if (deg < 247.5) return "\u897F\u5357";
  if (deg < 292.5) return "\u897F";
  return "\u897F\u5317";
}
function Minimal3D({ back }) {
  const [yaw, setYaw] = (0, import_react22.useState)(0);
  const [walk, setWalk] = (0, import_react22.useState)(0);
  const heading = headingOf((yaw % 360 + 360) % 360);
  const stage = {
    position: "absolute",
    left: "50%",
    top: "50%",
    width: 0,
    height: 0,
    transformStyle: "preserve-3d",
    transform: `translateZ(${walk}px) rotateX(-6deg) rotateY(${-yaw}deg)`
  };
  const plane = (style) => ({
    position: "absolute",
    transformStyle: "preserve-3d",
    ...style
  });
  return /* @__PURE__ */ (0, import_jsx_runtime30.jsxs)("div", { className: "page", style: { display: "flex", flexDirection: "column", height: "100%" }, children: [
    /* @__PURE__ */ (0, import_jsx_runtime30.jsx)(
      PageHeader,
      {
        title: "3D \u7070\u5EA6\u89C6\u56FE",
        backText: "\u8FD4\u56DE",
        onBack: back,
        right: /* @__PURE__ */ (0, import_jsx_runtime30.jsx)(GlassButton, { size: "sm", children: "\u56DE\u9E1F\u77B0" })
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime30.jsxs)(
      "div",
      {
        style: {
          position: "relative",
          flex: 1,
          minHeight: 320,
          overflow: "hidden",
          background: GRAY.sky
        },
        "aria-label": "3D \u7070\u5EA6\u89C6\u56FE",
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime30.jsx)("div", { style: { position: "absolute", inset: 0, perspective: "640px", perspectiveOrigin: "50% 48%" }, children: /* @__PURE__ */ (0, import_jsx_runtime30.jsxs)("div", { style: stage, children: [
            /* @__PURE__ */ (0, import_jsx_runtime30.jsx)(
              "div",
              {
                style: plane({
                  left: -700,
                  top: -700,
                  width: 1400,
                  height: 1400,
                  transform: "translateY(190px) rotateX(90deg)",
                  background: `linear-gradient(${GRAY.line} 1px, transparent 1px), linear-gradient(90deg, ${GRAY.line} 1px, transparent 1px), ${GRAY.floor}`,
                  backgroundSize: "60px 60px, 60px 60px, auto"
                })
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime30.jsx)(
              "div",
              {
                style: plane({
                  left: -700,
                  top: -220,
                  width: 1400,
                  height: 410,
                  transform: "translateX(-360px) rotateY(90deg)",
                  background: `linear-gradient(${GRAY.line} 1px, transparent 1px), ${GRAY.face}`,
                  backgroundSize: "100% 52px"
                })
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime30.jsx)(
              "div",
              {
                style: plane({
                  left: -700,
                  top: -220,
                  width: 1400,
                  height: 410,
                  transform: "translateX(360px) rotateY(90deg)",
                  background: `linear-gradient(${GRAY.line} 1px, transparent 1px), ${GRAY.face}`,
                  backgroundSize: "100% 52px"
                })
              }
            ),
            BUILDINGS.map((box) => /* @__PURE__ */ (0, import_jsx_runtime30.jsx)("div", { style: plane({ left: 0, top: 0, transformStyle: "flat" }), children: /* @__PURE__ */ (0, import_jsx_runtime30.jsx)(
              "div",
              {
                style: {
                  position: "absolute",
                  left: box.x - box.w / 2,
                  top: 190 - box.h,
                  width: box.w,
                  height: box.h,
                  transform: `translateZ(${box.z}px)`,
                  background: GRAY.face,
                  border: `1px solid ${GRAY.side}`
                }
              }
            ) }, `${box.x}-${box.z}`))
          ] }) }),
          /* @__PURE__ */ (0, import_jsx_runtime30.jsx)(
            "div",
            {
              "aria-hidden": "true",
              style: {
                position: "absolute",
                inset: 0,
                background: `radial-gradient(ellipse at 50% 48%, transparent 40%, ${GRAY.sky} 78%)`,
                pointerEvents: "none"
              }
            }
          )
        ]
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime30.jsx)(Glass, { tone: "card", radius: 12, style: { margin: "0 12px 12px" }, children: /* @__PURE__ */ (0, import_jsx_runtime30.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-1)", padding: "var(--w-space-2)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime30.jsx)(Cap2, { children: "\u62D6\u52A8\u753B\u9762\u8F6C\u5411 \xB7 \u7535\u8111\u53EF\u7528\u65B9\u5411\u952E\u8D70\u52A8 \xB7 \u624B\u673A\u53EF\u9009\u9640\u87BA\u4EEA\u73AF\u89C6" }),
      /* @__PURE__ */ (0, import_jsx_runtime30.jsxs)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime30.jsx)(Cap2, { children: "\u5168\u666F" }),
        /* @__PURE__ */ (0, import_jsx_runtime30.jsx)("div", { style: { flex: 1 }, children: /* @__PURE__ */ (0, import_jsx_runtime30.jsx)(
          GlassSlider,
          {
            label: "\u5168\u666F",
            value: (yaw % 360 + 360) % 360 / 360,
            onChange: (next) => setYaw(next * 360),
            width: 280,
            format: (value) => `${Math.round(value * 360)}\xB0`
          }
        ) }),
        /* @__PURE__ */ (0, import_jsx_runtime30.jsx)(Cap2, { style: { width: 34, textAlign: "center" }, children: heading })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime30.jsxs)("div", { style: { display: "flex", flexWrap: "wrap", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime30.jsx)(GlassButton, { size: "sm", onClick: () => setYaw(yaw - 12.857), children: "\u2190 \u5DE6\u8F6C" }),
        /* @__PURE__ */ (0, import_jsx_runtime30.jsx)(GlassButton, { size: "sm", onClick: () => setWalk(walk + 16), children: "\u2191 \u524D\u8FDB" }),
        /* @__PURE__ */ (0, import_jsx_runtime30.jsx)(GlassButton, { size: "sm", onClick: () => setWalk(walk - 10), children: "\u2193 \u540E\u9000" }),
        /* @__PURE__ */ (0, import_jsx_runtime30.jsx)(GlassButton, { size: "sm", onClick: () => setYaw(yaw + 12.857), children: "\u53F3\u8F6C \u2192" })
      ] })
    ] }) })
  ] });
}

// src/screens/games/pet.tsx
var import_jsx_runtime31 = __toESM(require_jsx_runtime(), 1);
function Pet({ back }) {
  return /* @__PURE__ */ (0, import_jsx_runtime31.jsxs)(
    "div",
    {
      style: {
        position: "relative",
        width: "100%",
        height: "100%",
        minHeight: "100%",
        overflow: "hidden",
        background: "color-mix(in oklab, var(--w-ink-display) 82%, var(--w-bg))",
        color: "var(--w-ink-on-signal)"
      },
      children: [
        /* @__PURE__ */ (0, import_jsx_runtime31.jsx)(
          "button",
          {
            type: "button",
            "aria-label": "\u5C0F\u96E8",
            onClick: back,
            style: {
              position: "absolute",
              top: 58,
              bottom: 50,
              left: 0,
              right: 0,
              border: 0,
              background: "none",
              padding: 0,
              cursor: "pointer",
              display: "flex",
              alignItems: "center",
              justifyContent: "center"
            },
            children: /* @__PURE__ */ (0, import_jsx_runtime31.jsx)(
              "span",
              {
                "aria-hidden": "true",
                style: {
                  width: 220,
                  height: 300,
                  border: "1px dashed color-mix(in oklab, var(--w-ink-on-signal) 45%, transparent)",
                  borderRadius: 14
                }
              }
            )
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime31.jsx)(
          "span",
          {
            style: {
              position: "absolute",
              left: "50%",
              transform: "translateX(-50%)",
              bottom: 6,
              padding: "5px 14px",
              borderRadius: 12,
              background: "color-mix(in oklab, var(--w-ink-display) 70%, transparent)",
              fontSize: "var(--w-size-caption)",
              color: "var(--w-ink-on-signal)"
            },
            children: "\u5C0F\u96E8"
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime31.jsx)(
          "button",
          {
            type: "button",
            onClick: back,
            style: {
              position: "absolute",
              left: 8,
              bottom: 8,
              height: 28,
              padding: "0 10px",
              borderRadius: 14,
              border: 0,
              background: "color-mix(in oklab, var(--w-ink-display) 70%, transparent)",
              color: "color-mix(in oklab, var(--w-ink-on-signal) 78%, transparent)",
              font: "inherit",
              fontSize: "var(--w-size-caption)",
              cursor: "pointer"
            },
            children: "\u2190 \u8FD4\u56DE"
          }
        )
      ]
    }
  );
}

// src/screens/games/shop.tsx
var import_react23 = __toESM(require_react(), 1);
var import_jsx_runtime32 = __toESM(require_jsx_runtime(), 1);
var SEED3 = [
  { cat: "\u996E\u54C1", name: "\u62DB\u724C\u73CD\u73E0\u5976\u8336", desc: "\u7ECF\u5178\u9ED1\u7CD6\u73CD\u73E0\uFF0C\u6696\u5FC3\u6696\u80C3", price: 16 },
  { cat: "\u996E\u54C1", name: "\u6768\u679D\u7518\u9732", desc: "\u8292\u679C\u897F\u67DA\uFF0C\u590F\u65E5\u6E05\u723D", price: 22 },
  { cat: "\u996E\u54C1", name: "\u751F\u6930\u62FF\u94C1", desc: "\u6930\u9999\u6D53\u90C1\uFF0C\u5496\u5561\u9187\u539A", price: 24 },
  { cat: "\u996E\u54C1", name: "\u871C\u6843\u4E4C\u9F99", desc: "\u871C\u6843\u9999\u6C14\u914D\u4E4C\u9F99\u8336\u5E95", price: 18 },
  { cat: "\u751C\u54C1", name: "\u8349\u8393\u86CB\u7CD5", desc: "\u8349\u8393\u53E3\u5473\u7684\u86CB\u7CD5\uFF0C\u751C\u5EA6\u521A\u597D", price: 28 },
  { cat: "\u751C\u54C1", name: "\u63D0\u62C9\u7C73\u82CF", desc: "\u5496\u5561\u9152\u9999\uFF0C\u5165\u53E3\u5373\u5316", price: 26 },
  { cat: "\u751C\u54C1", name: "\u62B9\u8336\u51B0\u6DC7\u6DCB", desc: "\u5FAE\u82E6\u56DE\u7518\uFF0C\u7EC6\u817B\u987A\u6ED1", price: 15 },
  { cat: "\u751C\u54C1", name: "\u9A6C\u5361\u9F99\u793C\u76D2", desc: "\u6CD5\u5F0F\u5C11\u5973\u5FC3\uFF0C\u516D\u8272\u516D\u5473", price: 32 },
  { cat: "\u9910\u996E", name: "\u7EA2\u70E7\u8089\u5957\u9910", desc: "\u80A5\u800C\u4E0D\u817B\uFF0C\u4E0B\u996D\u795E\u5668", price: 38 },
  { cat: "\u9910\u996E", name: "\u9EBB\u8FA3\u5C0F\u9F99\u867E", desc: "\u590F\u591C\u5927\u6392\u6863\u6807\u914D", price: 68 },
  { cat: "\u9910\u996E", name: "\u756A\u8304\u7092\u86CB\u5355\u4EBA\u4EFD", desc: "\u5BB6\u5E38\u5473\uFF0C\u6696\u6696\u7684", price: 18 },
  { cat: "\u65B9\u4FBF\u98DF\u54C1", name: "\u87BA\u86F3\u7C89", desc: "\u9178\u8FA3\u9C9C\u9999\uFF0C\u8D8A\u5403\u8D8A\u4E0A\u763E", price: 14 },
  { cat: "\u65B9\u4FBF\u98DF\u54C1", name: "\u81EA\u70ED\u5C0F\u706B\u9505", desc: "\u52A0\u73ED\u6DF1\u591C\u6551\u661F", price: 22 },
  { cat: "\u836F\u7269", name: "\u611F\u5192\u7075\u51B2\u5242", desc: "\u6362\u5B63\u611F\u5192\uFF0C\u4E00\u5305\u89C1\u6548", price: 28 },
  { cat: "\u836F\u7269", name: "\u521B\u53EF\u8D34\u4E00\u76D2", desc: "\u5C0F\u4F24\u53E3\u5FC5\u5907", price: 8 },
  { cat: "\u836F\u7269", name: "\u7EF4\u751F\u7D20C\u6CE1\u817E\u7247", desc: "\u589E\u5F3A\u62B5\u6297\u529B", price: 25 },
  { cat: "\u60C5\u8DA3\u7528\u54C1", name: "\u9999\u85B0\u8721\u70DB", desc: "\u6C1B\u56F4\u611F\u62C9\u6EE1", price: 39 },
  { cat: "\u60C5\u8DA3\u7528\u54C1", name: "\u771F\u4E1D\u773C\u7F69", desc: "\u52A9\u7720\u795E\u5668", price: 29 },
  { cat: "\u793C\u54C1", name: "\u73AB\u7470\u82B1\u675F", desc: "11\u6735\u7EA2\u73AB\u7470\uFF0C\u8868\u767D\u5FC5\u5907", price: 99 },
  { cat: "\u793C\u54C1", name: "\u6BDB\u7ED2\u73A9\u5177\u718A", desc: "\u62B1\u62B1\u66F4\u6E29\u6696", price: 45 },
  { cat: "\u793C\u54C1", name: "\u661F\u7A7A\u6295\u5F71\u706F", desc: "\u6EE1\u5C4B\u661F\u6CB3", price: 76 },
  { cat: "\u73A9\u5177", name: "\u4E50\u9AD8\u79EF\u6728", desc: "\u62FC\u51FA\u60F3\u8C61\u529B", price: 128 },
  { cat: "\u73A9\u5177", name: "\u76F2\u76D2", desc: "\u62C6\u5F00\u6709\u60CA\u559C", price: 29 },
  { cat: "\u8282\u65E5\u7279\u6B8A\u5546\u54C1", name: "\u751F\u65E5\u86CB\u7CD5", desc: "\u4E13\u5C5E\u751F\u65E5\u86CB\u7CD5\uFF0C\u53EF\u5B9A\u5236", price: 88 },
  { cat: "\u8282\u65E5\u7279\u6B8A\u5546\u54C1", name: "\u5E74\u8D27\u5927\u793C\u5305", desc: "\u6625\u8282\u5E74\u5473\u5341\u8DB3", price: 128 },
  {
    cat: "\u573A\u666F\u5238",
    name: "\u6E38\u4E50\u56ED\u95E8\u7968",
    desc: "\u89E3\u9501\u3010\u6E38\u4E50\u56ED\u3011\u573A\u666F\uFF0C\u65CB\u8F6C\u6728\u9A6C\u3001\u8FC7\u5C71\u8F66\uFF0C\u548C\u5C0F\u96E8\u5C3D\u60C5\u73A9",
    price: 168,
    ticket: true
  },
  { cat: "\u573A\u666F\u5238", name: "\u7535\u5F71\u9662\u53CC\u4EBA\u7968", desc: "\u89E3\u9501\u3010\u7535\u5F71\u9662\u3011\u573A\u666F\uFF0C\u62B1\u7740\u7206\u7C73\u82B1\u770B\u665A\u573A\u7535\u5F71", price: 98, ticket: true },
  { cat: "\u573A\u666F\u5238", name: "\u6E29\u6CC9\u5EA6\u5047\u5238", desc: "\u89E3\u9501\u3010\u6E29\u6CC9\u6C64\u5C4B\u3011\u573A\u666F\uFF0C\u4E00\u8D77\u6CE1\u6E29\u6CC9\u770B\u661F\u661F", price: 198, ticket: true }
];
var CATEGORIES = ["\u5168\u90E8", ...Array.from(new Set(SEED3.map((item) => item.cat))).sort((a, b) => a < b ? -1 : a > b ? 1 : 0)];
var priceText = (price) => price < 0 ? "\u4EF7\u683C\u5F02\u5E38" : `${price.toLocaleString("en-US")} \u5E01`;
function Shop({ back }) {
  const [query, setQuery] = (0, import_react23.useState)("");
  const [category, setCategory] = (0, import_react23.useState)("\u5168\u90E8");
  const [aiPrompt, setAiPrompt] = (0, import_react23.useState)("");
  const [repo, setRepo] = (0, import_react23.useState)("");
  const [bodyOpen, setBodyOpen] = (0, import_react23.useState)(false);
  const items = SEED3.filter((item) => category === "\u5168\u90E8" || item.cat === category).filter(
    (item) => query.trim().length === 0 ? true : item.name.includes(query.trim()) || item.desc.includes(query.trim())
  );
  return /* @__PURE__ */ (0, import_jsx_runtime32.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(
      PageHeader,
      {
        title: "\u7F8E\u4E86\u4E48",
        onBack: back,
        right: /* @__PURE__ */ (0, import_jsx_runtime32.jsxs)(import_jsx_runtime32.Fragment, { children: [
          /* @__PURE__ */ (0, import_jsx_runtime32.jsx)("button", { type: "button", className: "k-textbtn", style: { fontSize: "var(--w-size-caption)" }, onClick: () => setBodyOpen(true), children: "\u8EAB\u4F53\u72B6\u6001" }),
          /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(
            "span",
            {
              style: {
                display: "inline-flex",
                alignItems: "center",
                height: 24,
                padding: "0 10px",
                borderRadius: 99,
                background: "var(--w-surface-tint)",
                fontSize: "var(--w-size-body)",
                color: "var(--w-ink-body)",
                whiteSpace: "nowrap"
              },
              children: "\u4EB2\u5BC6\u5E01 0"
            }
          )
        ] })
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime32.jsx)("div", { style: { background: "var(--w-tint-bar)" }, children: /* @__PURE__ */ (0, import_jsx_runtime32.jsxs)(
      "div",
      {
        style: {
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          gap: "var(--w-space-3)",
          padding: "var(--w-space-2) var(--w-pad-page)"
        },
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(Cap2, { tone: "body", style: { whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }, children: "\u5C0F\u6E38\u620F\u6218\u7EE9\uFF1A0 \u80DC 0 \u8D1F" }),
          /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(Cap2, { style: { whiteSpace: "nowrap" }, children: "\u80DC\u5C40 +30 \u4EB2\u5BC6\u5E01 \xB7 \u8D25\u5C40 +10" })
        ]
      }
    ) }),
    /* @__PURE__ */ (0, import_jsx_runtime32.jsxs)("div", { className: "screen", style: { gap: "var(--w-space-3)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(Glass, { tone: "card", radius: 12, children: /* @__PURE__ */ (0, import_jsx_runtime32.jsxs)("div", { style: { display: "flex", flexDirection: "column", padding: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime32.jsx)("div", { style: { padding: "10px 0", borderBottom: "1px solid var(--w-hairline)" }, children: /* @__PURE__ */ (0, import_jsx_runtime32.jsx)("span", { style: { fontSize: "var(--w-size-body)", color: "var(--w-ink-body)" }, children: "AI \u9009\u54C1 \xB7 \u60F3\u8981\u5565\u8BF4\u5565\uFF0C\u5B9E\u65F6\u4E0A\u65B0" }) }),
        /* @__PURE__ */ (0, import_jsx_runtime32.jsxs)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-1)", padding: "10px 0" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(
            InlineField,
            {
              value: aiPrompt,
              onChange: setAiPrompt,
              placeholder: "\u4F8B\uFF1A\u5C0F\u96E8\u559C\u6B22\u7684\u8349\u8393\u5473\u996E\u54C1\u3001\u751F\u65E5\u86CB\u7CD5\u2026",
              fontSize: "var(--w-size-caption)",
              style: { flex: 1 }
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(GlassButton, { size: "sm", children: "\u751F\u6210" })
        ] })
      ] }) }),
      /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(Glass, { tone: "card", radius: 12, children: /* @__PURE__ */ (0, import_jsx_runtime32.jsxs)("div", { style: { display: "flex", flexDirection: "column", padding: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime32.jsx)("div", { style: { padding: "10px 0", borderBottom: "1px solid var(--w-hairline)" }, children: /* @__PURE__ */ (0, import_jsx_runtime32.jsx)("span", { style: { fontSize: "var(--w-size-body)", color: "var(--w-ink-body)" }, children: "GitHub \u4ED3\u5E93\u540C\u6B65 \xB7 \u4ED3\u5E93\u5185 shop/catalog.json \u4F1A\u81EA\u52A8\u4E0A\u67B6" }) }),
        /* @__PURE__ */ (0, import_jsx_runtime32.jsxs)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-1)", padding: "10px 0" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(
            InlineField,
            {
              value: repo,
              onChange: setRepo,
              placeholder: "owner/repo\uFF0C\u5982 my-username/my-shop",
              fontSize: "var(--w-size-caption)",
              style: { flex: 1 }
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(GlassButton, { size: "sm", children: "\u540C\u6B65" })
        ] })
      ] }) }),
      /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(Glass, { tone: "card", radius: 12, children: /* @__PURE__ */ (0, import_jsx_runtime32.jsx)("div", { style: { padding: "10px var(--w-space-2)" }, children: /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(InlineField, { value: query, onChange: setQuery, placeholder: "\u641C\u7D22\u5546\u54C1\u2026" }) }) }),
      /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(ToolRow, { children: CATEGORIES.map((name) => /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(
        Well3,
        {
          active: category === name,
          onClick: () => setCategory(name),
          style: { padding: "4px 12px", borderRadius: 99, border: "1px solid var(--w-hairline)" },
          children: name
        },
        name
      )) }),
      /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(Glass, { tone: "card", radius: 12, children: /* @__PURE__ */ (0, import_jsx_runtime32.jsx)("div", { style: { padding: "0 var(--w-space-2)" }, children: items.length === 0 ? /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(
        Empty,
        {
          title: "\u8D27\u67B6\u4E0A\u662F\u7A7A\u7684",
          reason: "\u641C\u7D22\u8BCD\u6216\u5206\u7C7B\u628A\u5546\u54C1\u5168\u8FC7\u6EE4\u6389\u4E86\uFF0C\u4E5F\u53EF\u80FD\u662F\u8FD8\u6CA1\u540C\u6B65\u8FC7\u4ED3\u5E93",
          hint: "\u6E05\u7A7A\u641C\u7D22\u3001\u70B9\u300C\u5168\u90E8\u300D\uFF0C\u6216\u8005\u7528\u4E0A\u9762\u7684 AI \u9009\u54C1\u73B0\u751F\u6210\u4E00\u4EF6"
        }
      ) : items.map((item) => /* @__PURE__ */ (0, import_jsx_runtime32.jsxs)("div", { style: { display: "flex", flexDirection: "column" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime32.jsxs)(
          "div",
          {
            style: {
              display: "flex",
              alignItems: "center",
              gap: "var(--w-space-3)",
              minHeight: "var(--w-row-h)",
              padding: "10px 0"
            },
            children: [
              /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(
                "div",
                {
                  style: {
                    flex: "0 0 auto",
                    width: 50,
                    height: 50,
                    borderRadius: "var(--w-radius-well)",
                    background: "var(--w-surface-tint)",
                    border: "1px solid var(--w-hairline)",
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "center",
                    fontSize: "var(--w-size-title)",
                    color: "var(--w-ink-muted)"
                  },
                  children: item.name.slice(0, 1)
                }
              ),
              /* @__PURE__ */ (0, import_jsx_runtime32.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 2, flex: 1, minWidth: 0 }, children: [
                /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(
                  "span",
                  {
                    style: {
                      fontSize: "var(--w-size-body)",
                      color: "var(--w-ink-body)",
                      whiteSpace: "nowrap",
                      overflow: "hidden",
                      textOverflow: "ellipsis"
                    },
                    title: item.name,
                    children: item.name
                  }
                ),
                /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(Cap2, { style: { display: "-webkit-box", WebkitLineClamp: 2, WebkitBoxOrient: "vertical", overflow: "hidden" }, children: item.desc }),
                /* @__PURE__ */ (0, import_jsx_runtime32.jsx)("div", { style: { display: "flex", flexWrap: "wrap", gap: "var(--w-space-1)" }, children: /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(Cap2, { style: { whiteSpace: "nowrap" }, children: item.cat }) })
              ] }),
              /* @__PURE__ */ (0, import_jsx_runtime32.jsxs)("div", { style: { display: "flex", flexDirection: "column", alignItems: "flex-end", gap: "var(--w-space-1)" }, children: [
                /* @__PURE__ */ (0, import_jsx_runtime32.jsx)("span", { style: { fontSize: "var(--w-size-body)", color: "var(--w-ink-display)" }, children: priceText(item.price) }),
                /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(GlassButton, { size: "sm", children: item.ticket ? "\u89E3\u9501" : "\u8D2D\u4E70" }),
                /* @__PURE__ */ (0, import_jsx_runtime32.jsxs)("div", { style: { display: "flex", gap: "var(--w-space-1)" }, children: [
                  /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(GlassButton, { size: "sm", children: "\u9001\u793C" }),
                  /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(GlassButton, { size: "sm", children: "\u4F7F\u7528" })
                ] })
              ] })
            ]
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime32.jsx)("div", { className: "k-sep", role: "presentation" })
      ] }, `${item.cat}-${item.name}`)) }) })
    ] }),
    bodyOpen ? /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(
      "div",
      {
        style: {
          position: "fixed",
          inset: 0,
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          background: "color-mix(in oklab, var(--w-ink-display) 42%, transparent)",
          zIndex: 20
        },
        children: /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(Glass, { tone: "card", radius: 12, style: { width: 320, maxWidth: "92vw" }, children: /* @__PURE__ */ (0, import_jsx_runtime32.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)", padding: "var(--w-space-3)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime32.jsx)("span", { style: { fontSize: "var(--w-size-title)", fontWeight: 600, color: "var(--w-ink-display)", textAlign: "center" }, children: "\u5C0F\u96E8\u7684\u8EAB\u4F53\u72B6\u6001" }),
          /* @__PURE__ */ (0, import_jsx_runtime32.jsxs)(
            "div",
            {
              style: {
                padding: "var(--w-space-2)",
                borderRadius: "var(--w-radius-well)",
                background: "var(--w-surface-tint)"
              },
              children: [
                /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(Section, { muted: true, children: "\u8FD8\u6CA1\u6709\u8EAB\u4F53\u72B6\u6001" }),
                /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(Cap2, { style: { display: "block", marginTop: 2 }, children: "\u8FD9\u4E00\u4EFD\u7531\u540E\u7AEF\u7684\u65E5\u7A0B\u4E0E\u60C5\u7EEA\u7B97\u51FA\u6765\uFF0C\u773C\u4E0B\u624B\u8FB9\u6CA1\u6709\u6570\u636E\u3002\u7B49\u540E\u53F0\u6302\u4E0A\u6765\uFF0C\u8FD9\u91CC\u4F1A\u663E\u793A\u5979\u6B64\u523B\u7684\u8EAB\u4F53\u72B6\u6001\u3002" })
              ]
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime32.jsx)(GlassButton, { size: "md", onClick: () => setBodyOpen(false), children: "\u5173\u95ED" })
        ] }) })
      }
    ) : null
  ] });
}

// src/screens/games/index.tsx
var screens3 = {
  game: Game,
  map: Map2,
  mapcreate: MapCreate,
  mapcapture: MapCapture,
  minimal3d: Minimal3D,
  flows: Flows,
  shop: Shop,
  "affection-level-up": AffectionLevelUp,
  "cg-view": CgView,
  pet: Pet
};

// src/screens/main/main.tsx
var import_react24 = __toESM(require_react(), 1);
var import_jsx_runtime33 = __toESM(require_jsx_runtime(), 1);
var WIDGETS = [
  { id: "date", title: "\u65E5\u671F", desc: "\u4ECA\u5929\u51E0\u53F7\u3001\u661F\u671F\u51E0\u3002", slot: "top", visible: true },
  { id: "location", title: "\u4F4D\u7F6E", desc: "\u4F60\u548C TA \u73B0\u5728\u6240\u5728\u7684\u5730\u70B9\u3002", slot: "left", visible: true },
  { id: "affection", title: "\u597D\u611F", desc: "\u597D\u611F\u5EA6\u4E0E\u5F53\u524D\u7B49\u7EA7\u3002", slot: "left", visible: true },
  { id: "trust", title: "\u4FE1\u4EFB", desc: "\u4FE1\u4EFB\u5EA6\u8BFB\u6570\u3002", slot: "left", visible: true },
  { id: "energy", title: "\u7CBE\u529B", desc: "TA \u4ECA\u5929\u8FD8\u5269\u591A\u5C11\u529B\u6C14\u3002", slot: "left", visible: true },
  { id: "balance", title: "\u4F59\u989D", desc: "\u94B1\u5305\u91CC\u8FD8\u5269\u591A\u5C11\u3002", slot: "left", visible: true },
  { id: "brain", title: "\u5FC3\u4E8B", desc: "\u5927\u8111\u9884\u5224\u8BFB\u51FA\u7684\u4E00\u53E5\u300CTA \u73B0\u5728\u5728\u60F3\u4EC0\u4E48\u300D\u3002", slot: "left", visible: true },
  { id: "mood", title: "\u60C5\u7EEA", desc: "\u6B64\u523B\u8868\u60C5\u5BF9\u5E94\u7684\u60C5\u7EEA\u3002", slot: "left", visible: false }
];
var TOP_BUTTONS = [
  { id: "save", label: "\u5B58\u6863" },
  { id: "menu", label: "\u83DC\u5355" },
  { id: "phone", label: "\u624B\u673A" },
  { id: "map", label: "\u5730\u56FE" },
  { id: "ai", label: "AI" },
  { id: "hide", label: "\u6536\u8D77" }
];
var CHARACTER_NAME = "\u6C88\u781A";
var LOCATION_LABEL = "\u7B2C\u4E09\u7AE0 \xB7 \u706F\u4E0B\u8865\u4E66";
var WEEK_CN = ["\u65E5", "\u4E00", "\u4E8C", "\u4E09", "\u56DB", "\u4E94", "\u516D"];
function dateLabel(now) {
  return `${now.getMonth() + 1}\u6708${now.getDate()}\u65E5 \u5468${WEEK_CN[now.getDay()]}`;
}
function timeLabel(now) {
  return `${String(now.getHours()).padStart(2, "0")}:${String(now.getMinutes()).padStart(2, "0")}`;
}
function widgetValue(id, day, clock) {
  switch (id) {
    case "clock":
      return clock;
    case "date":
      return day;
    case "weather":
      return "\u6674\u6717";
    case "location":
      return LOCATION_LABEL;
    case "affection":
      return "Lv.3 \xB7 \u719F\u6089";
    case "trust":
      return "30/100";
    case "energy":
      return "100%";
    case "balance":
      return "\xA4 1000";
    case "mood":
      return "\u5E73\u9759";
    default:
      return "";
  }
}
function Main({ navigate }) {
  const now = /* @__PURE__ */ new Date();
  const day = dateLabel(now);
  const clock = timeLabel(now);
  const [widgets, setWidgets] = (0, import_react24.useState)(WIDGETS);
  const [buttons, setButtons] = (0, import_react24.useState)(
    () => Object.fromEntries(TOP_BUTTONS.map((b) => [b.id, true]))
  );
  const [chatMode, setChatMode] = (0, import_react24.useState)(false);
  const [hiddenPanels, setHiddenPanels] = (0, import_react24.useState)(false);
  const [menuOpen, setMenuOpen] = (0, import_react24.useState)(false);
  const [widgetPanel, setWidgetPanel] = (0, import_react24.useState)(false);
  const [giftOpen, setGiftOpen] = (0, import_react24.useState)(false);
  const [giftMode, setGiftMode] = (0, import_react24.useState)(true);
  const [minimapOpen, setMinimapOpen] = (0, import_react24.useState)(true);
  const [blackout, setBlackout] = (0, import_react24.useState)(false);
  const [calibrationBlocked] = (0, import_react24.useState)(false);
  const [backendMode] = (0, import_react24.useState)(false);
  const [input, setInput] = (0, import_react24.useState)("");
  const [messages, setMessages] = (0, import_react24.useState)([]);
  const stateButtons = [];
  const modeLabel = chatMode ? "\u5207\u56DE Galgame" : "\u5207\u5230\u804A\u5929\u6A21\u5F0F";
  const topWidgets = widgets.filter((w) => w.slot === "top" && w.visible);
  const leftWidgets = widgets.filter((w) => w.slot === "left" && w.visible && w.id !== "brain");
  const last = messages.length > 0 ? messages[messages.length - 1] : null;
  const send = () => {
    const text = input.trim();
    if (!text) return;
    setMessages((prev) => [...prev, { id: Date.now(), mine: true, text }]);
    setInput("");
  };
  const toggleButton = (id) => setButtons((prev) => ({ ...prev, [id]: !prev[id] }));
  const move = (id, dir) => {
    setWidgets((prev) => {
      const row = prev.find((w) => w.id === id);
      if (!row) return prev;
      const same = prev.filter((w) => w.slot === row.slot);
      const at = same.indexOf(row);
      const target = same[at + dir];
      if (!target) return prev;
      const next = [...prev];
      const from = next.indexOf(row);
      const to = next.indexOf(target);
      next[from] = target;
      next[to] = row;
      return next;
    });
  };
  const flipSlot = (id) => {
    setWidgets(
      (prev) => prev.map((w) => w.id === id ? { ...w, slot: w.slot === "top" ? "left" : "top" } : w)
    );
  };
  const setVisible = (id, on) => {
    setWidgets((prev) => prev.map((w) => w.id === id ? { ...w, visible: on } : w));
  };
  return /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("div", { className: "mg", children: [
    hiddenPanels ? null : /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)(Glass, { as: "header", tone: "bar", radius: 0, className: "mg__top", children: [
      /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("div", { className: "mg__top-widgets", children: topWidgets.map((w) => /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("div", { className: "mg__w", children: [
        /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("span", { className: "mg__w-title", children: w.title }),
        /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("span", { className: `mg__w-value${w.id === "clock" ? " mg__w-value--clock" : ""}`, children: widgetValue(w.id, day, clock) })
      ] }, w.id)) }),
      /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("div", { className: "mg__top-actions", children: [
        TOP_BUTTONS.filter((b) => buttons[b.id]).map((b) => /* @__PURE__ */ (0, import_jsx_runtime33.jsx)(
          "button",
          {
            type: "button",
            className: "m-btn",
            onClick: () => {
              if (b.id === "menu") setMenuOpen(true);
              if (b.id === "phone") navigate("/phone");
              if (b.id === "hide") setHiddenPanels(true);
            },
            title: b.id === "hide" ? "\u9690\u85CF\u4FE1\u606F\u680F\u4E0E\u9876\u90E8\uFF08Esc\uFF09" : b.label,
            children: b.label
          },
          b.id
        )),
        /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn", onClick: () => navigate("/superMonitor"), title: "\u67E5\u770B\u5F53\u524D\u5B58\u6863\u7684\u8D85\u7EA7\u76D1\u6D4B", children: "\u76D1\u6D4B" }),
        /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn m-btn--accent", onClick: () => setWidgetPanel(true), title: "\u9009\u62E9\u9876\u680F\u4E0E\u5DE6\u680F\u663E\u793A\u4EC0\u4E48", children: "\u7EC4\u4EF6" })
      ] })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("div", { className: "mg__body", "data-chat": chatMode ? "1" : void 0, "data-left": hiddenPanels ? "0" : "1", children: [
      hiddenPanels ? null : /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)(Glass, { as: "aside", tone: "bar", radius: 0, className: "mg__left", children: [
        /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("span", { className: "mg__left-title", children: "\u4FE1\u606F" }),
        /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("div", { className: "mg__left-list", children: leftWidgets.length === 0 ? /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("p", { className: "mg__left-empty", children: "\u8FD8\u6CA1\u6709\u663E\u793A\u7684\u4FE1\u606F\u3002\u6253\u5F00\u300C\u7EC4\u4EF6\u300D\uFF0C\u628A\u60F3\u770B\u7684\u7559\u5728\u8FD9\u4E00\u680F\u3002" }) : leftWidgets.map((w) => /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("div", { className: "mg__w", children: [
          /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("span", { className: "mg__w-title", children: w.title }),
          /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("span", { className: "mg__w-value", children: widgetValue(w.id, day, clock) })
        ] }, w.id)) }),
        /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "mg__footer-btn", onClick: () => setWidgetPanel(true), children: "\u7EC4\u4EF6" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("section", { className: "mg__stage", children: [
        /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("span", { className: "mg__stage-dim", "aria-hidden": "true" }),
        /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "mg__nav mg__nav--prev", disabled: true, "aria-label": "\u4E0A\u4E00\u4E2A\u5730\u70B9", children: "\u25C0" }),
        /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "mg__nav mg__nav--next", disabled: true, "aria-label": "\u4E0B\u4E00\u4E2A\u5730\u70B9", children: "\u25B6" }),
        /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("div", { className: "mg__corner", children: minimapOpen ? /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)(Glass, { as: "div", tone: "card", radius: 16, className: "mg__minimap", children: [
          /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("div", { className: "mg__mm-head", children: [
            /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("div", { children: [
              /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("div", { className: "mg__mm-title", children: LOCATION_LABEL }),
              /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("div", { className: "mg__mm-sub", children: "\u9644\u8FD1" })
            ] }),
            /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn m-btn--sm m-btn--ghost", onClick: () => setMinimapOpen(false), children: "\u6536\u8D77" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("div", { className: "mg__mm-canvas", "aria-hidden": "true" }),
          /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("div", { className: "mg__mm-list", children: /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("span", { className: "mg__mm-empty", children: "\u9644\u8FD1\u6CA1\u522B\u7684\u5730\u65B9" }) }),
          /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn m-btn--sm m-btn--block", onClick: () => navigate("/map"), children: "\u6253\u5F00\u5927\u5730\u56FE" })
        ] }) : /* @__PURE__ */ (0, import_jsx_runtime33.jsx)(
          Glass,
          {
            as: "button",
            type: "button",
            tone: "card",
            radius: 14,
            className: "mg__minimap mg__mm-chip",
            style: { width: 60, height: 60 },
            onClick: () => setMinimapOpen(true),
            "aria-label": "\u5C55\u5F00\u5C0F\u5730\u56FE",
            children: /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("span", { className: "mg__mm-dot", "aria-hidden": "true" })
          }
        ) }),
        /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("div", { className: "mg__nosprite", children: [
          /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("span", { className: "mg__nosprite-mark", children: CHARACTER_NAME.slice(0, 1) }),
          /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("span", { className: "mg__nosprite-name", children: CHARACTER_NAME })
        ] }),
        chatMode ? null : /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)(Glass, { as: "section", tone: "bar", radius: 20, className: "mg__gal", children: [
          /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("div", { className: "mg__gal-title", children: [
            /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("span", { className: "mg__gal-chip", children: CHARACTER_NAME }),
            /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn m-btn--sm", onClick: () => setChatMode(true), children: modeLabel }),
            /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("span", { className: "mg__gal-speaker", children: last ? last.mine ? "\u4F60" : CHARACTER_NAME : "" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("div", { className: `mg__gal-history${last ? "" : " mg__gal-history--empty"}`, children: last ? last.text : "\u8FD9\u4E00\u5C40\u8FD8\u6CA1\u6709\u8BF4\u8FC7\u8BDD\u3002\u5728\u4E0B\u9762\u8BF4\u4E00\u53E5\uFF0CTA \u4F1A\u5728\u8FD9\u91CC\u56DE\u7B54\u4F60\u3002" }),
          /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("div", { className: "mg__input", children: [
            /* @__PURE__ */ (0, import_jsx_runtime33.jsx)(
              "input",
              {
                value: input,
                placeholder: "\u8F93\u5165\u4F60\u60F3\u8BF4\u7684\u8BDD...",
                "aria-label": "\u8F93\u5165\u4F60\u60F3\u8BF4\u7684\u8BDD",
                onChange: (event) => setInput(event.target.value),
                onKeyDown: (event) => {
                  if (event.key === "Enter") send();
                }
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-ibtn", children: "\u6587\u4EF6" }),
            /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-ibtn", onClick: () => setGiftOpen(true), children: "\u793C\u7269" }),
            /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-ibtn", children: "\u8BED\u97F3" }),
            /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "mg__send", onClick: send, children: "\u53D1\u9001" })
          ] })
        ] }),
        blackout ? /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("div", { className: "mg__blackout", children: /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("div", { className: "mg__blackout-inner", children: [
          /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("p", { children: "\uFF08\u9ED1\u5C4F\uFF09" }),
          /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn", onClick: () => setBlackout(false), children: "\u9000\u51FA\u9ED1\u5C4F" })
        ] }) }) : null,
        hiddenPanels ? /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn m-btn--sm mg__unhide", onClick: () => setHiddenPanels(false), title: "\u6062\u590D\u4FE1\u606F\u680F\u4E0E\u9876\u90E8\uFF08Esc\uFF09", children: "\u5C55\u5F00\u754C\u9762" }) : null
      ] }),
      chatMode ? /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)(Glass, { as: "aside", tone: "card", radius: 0, className: "mg__chat", children: [
        /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("div", { className: "mg__chat-head", children: [
          stateButtons.length > 0 ? /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("div", { className: "mg__state-row", children: stateButtons.map((label) => /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn m-btn--sm", children: label }, label)) }) : null,
          /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("div", { className: "mg__quick", children: [
            /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn", children: "\u6478\u6478\u5934" }),
            /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn", children: "\u62B1\u62B1" }),
            /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn", children: "\u4EB2\u543B" }),
            /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn m-btn--mode", onClick: () => setChatMode(false), children: chatMode ? "\u5207\u56DE Galgame" : "\u5207\u5230\u804A\u5929\u6A21\u5F0F" })
          ] })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("div", { className: "mg__chat-list", children: messages.length === 0 ? /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("p", { className: "mg__chat-empty", children: [
          "\u8FD8\u6CA1\u6709\u5BF9\u8BDD\u3002",
          /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("br", {}),
          "\u5728\u4E0B\u9762\u8BF4\u4E00\u53E5\uFF0C\u6216\u5148\u70B9\u4E00\u4E2A\u5FEB\u6377\u52A8\u4F5C\u3002"
        ] }) : messages.map((m) => /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("div", { className: `mg__bubble mg__bubble--${m.mine ? "mine" : "theirs"}`, children: m.text }, m.id)) }),
        /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("div", { className: "mg__input", children: [
          /* @__PURE__ */ (0, import_jsx_runtime33.jsx)(
            "input",
            {
              value: input,
              placeholder: "\u8F93\u5165...",
              "aria-label": "\u8F93\u5165\u6D88\u606F",
              onChange: (event) => setInput(event.target.value),
              onKeyDown: (event) => {
                if (event.key === "Enter") send();
              }
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-ibtn", children: "\u6587\u4EF6" }),
          /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-ibtn", children: "\u8868\u60C5" }),
          /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-ibtn", onClick: () => setGiftOpen(true), children: "\u793C\u7269" }),
          /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-ibtn", children: "\u8BED\u97F3" }),
          /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "mg__send", onClick: send, children: "\u53D1\u9001" })
        ] })
      ] }) : null
    ] }),
    menuOpen ? /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("div", { className: "mg__overlay", onClick: () => setMenuOpen(false), children: /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)(Glass, { as: "div", tone: "card", radius: 16, className: "mg__dialog mg__dialog--menu", onClick: (event) => event.stopPropagation(), children: [
      /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("h2", { className: "mg__menu-title", children: "\u83DC\u5355" }),
      /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn m-btn--block", onClick: () => navigate("/settings"), children: "\u8BBE\u7F6E" }),
      /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn m-btn--block", onClick: () => navigate("/phone"), children: "\u624B\u673A" }),
      /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn m-btn--block", children: "\u672C\u5C40\u8BBE\u5B9A" }),
      /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn m-btn--block", onClick: () => navigate("/save"), children: "\u5B58\u6863\u7BA1\u7406" }),
      /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn m-btn--block", children: "\u684C\u5BA0\u6A21\u5F0F" }),
      /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn m-btn--block m-btn--danger", onClick: () => navigate("/"), children: "\u56DE\u6807\u9898" }),
      /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn m-btn--block m-btn--ghost", onClick: () => setMenuOpen(false), children: "\u5173\u95ED" })
    ] }) }) : null,
    widgetPanel ? /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("div", { className: "mg__overlay", onClick: () => setWidgetPanel(false), children: /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)(
      Glass,
      {
        as: "div",
        tone: "card",
        radius: 18,
        className: "mg__dialog",
        onClick: (event) => event.stopPropagation(),
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("div", { className: "mg__dlg-head", children: [
            /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("div", { children: [
              /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("h2", { className: "mg__dlg-title", children: "\u7EC4\u4EF6" }),
              /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("p", { className: "mg__dlg-note", children: "\u60F3\u770B\u7684\u7559\u5728\u680F\u4E0A\uFF0C\u6682\u65F6\u4E0D\u770B\u7684\u6536\u8D77\u6765\uFF1B\u6539\u52A8\u5373\u65F6\u4FDD\u5B58\u3002" })
            ] }),
            /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn m-btn--sm", onClick: () => setWidgetPanel(false), children: "\u5173\u95ED" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("span", { className: "mg__hint", children: "\u9876\u680F\u6309\u94AE" }),
          /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("div", { className: "mg__chips", children: TOP_BUTTONS.map((b) => /* @__PURE__ */ (0, import_jsx_runtime33.jsx)(
            "button",
            {
              type: "button",
              className: "m-btn m-btn--sm",
              "aria-pressed": buttons[b.id],
              onClick: () => toggleButton(b.id),
              children: b.label
            },
            b.id
          )) }),
          /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("div", { className: "mg__wcols", children: ["top", "left"].map((slot) => /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("div", { className: "mg__wcol", children: [
            /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("span", { className: "mg__wcol-title", children: slot === "top" ? "\u9876\u680F" : "\u5DE6\u680F" }),
            /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("div", { className: "mg__wscroll", children: widgets.filter((w) => w.slot === slot).map((w) => /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("div", { className: "mg__wrow", children: [
              /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("label", { children: [
                /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("span", { className: "mg__wrow-title", children: w.title }),
                /* @__PURE__ */ (0, import_jsx_runtime33.jsx)(
                  GlassSwitch,
                  {
                    label: w.title,
                    checked: w.visible,
                    onChange: (on) => setVisible(w.id, on)
                  }
                )
              ] }),
              /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("span", { className: "mg__wrow-desc", children: w.desc }),
              /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("div", { className: "mg__wrow-acts", children: [
                /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn m-btn--square", onClick: () => move(w.id, -1), title: "\u4E0A\u79FB", children: "\u2191" }),
                /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn m-btn--square", onClick: () => move(w.id, 1), title: "\u4E0B\u79FB", children: "\u2193" }),
                /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn m-btn--xs", onClick: () => flipSlot(w.id), children: w.slot === "top" ? "\u79FB\u5230\u5DE6\u680F" : "\u79FB\u5230\u9876\u680F" })
              ] })
            ] }, w.id)) })
          ] }, slot)) }),
          /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("span", { className: "mg__hint", children: "\u7A84\u5C4F\u9876\u680F\u53EA\u6392\u524D\u4E09\u4E2A\u9876\u680F\u7EC4\u4EF6\uFF0C\u5176\u4F59\u4ECD\u5728\u9762\u677F\u91CC\u968F\u65F6\u8C03\u3002" })
        ]
      }
    ) }) : null,
    giftOpen ? /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("div", { className: "mg__overlay", onClick: () => setGiftOpen(false), children: /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)(
      Glass,
      {
        as: "div",
        tone: "card",
        radius: 18,
        className: "mg__dialog mg__dialog--gift",
        onClick: (event) => event.stopPropagation(),
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("h2", { className: "mg__dlg-title", style: { textAlign: "center" }, children: [
            "\u793C\u7269\u7BB1",
            " "
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("p", { className: "mg__dlg-note", style: { textAlign: "center" }, children: "\u70B9\u4E00\u4E0B\u9001\u51FA\u5FC3\u610F\uFF0C\u5C0F\u96E8\u4F1A\u6536\u4E0B\u5E76\u56DE\u5E94" }),
          /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("div", { className: "mg__gift-tabs", children: [
            /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)(
              "button",
              {
                type: "button",
                className: `m-btn${giftMode ? " m-btn--accent" : ""}`,
                onClick: () => setGiftMode(true),
                children: [
                  " ",
                  "\u9001\u793C"
                ]
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime33.jsx)(
              "button",
              {
                type: "button",
                className: `m-btn${giftMode ? "" : " m-btn--accent"}`,
                onClick: () => setGiftMode(false),
                children: "\u4F7F\u7528"
              }
            )
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("div", { className: "mg__gift-body", children: /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("span", { children: "\u8FD8\u6CA1\u6709\u5DF2\u8D2D\u5546\u54C1\uFF0C\u5148\u53BB\u5546\u5E97\u901B\u901B\u5427\uFF5E" }) }),
          /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn m-btn--block m-btn--ghost", onClick: () => setGiftOpen(false), children: "\u5173\u95ED" })
        ]
      }
    ) }) : null,
    calibrationBlocked ? /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("div", { className: "mg__overlay", children: /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)(Glass, { as: "div", tone: "card", radius: 18, className: "mg__dialog mg__dialog--menu", style: { width: "min(520px, 100%)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("h2", { className: "mg__calib-title", children: "\u8FD9\u5F20\u80CC\u666F\u8FD8\u6CA1\u6709\u6807\u5B9A" }),
      /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("p", { className: "mg__calib-body", children: "\u6807\u5B9A\u786E\u5B9A\u4E24\u4EF6\u4E8B\uFF1A\u89D2\u8272\u80FD\u7AD9\u5728\u54EA\u91CC\uFF08\u843D\u811A\u70B9\uFF09\uFF0C\u4EE5\u53CA\u8FD1\u5904\u8FDC\u5904\u8BE5\u600E\u4E48\u7F29\u653E\uFF08\u7F29\u653E\u6807\u5B9A\uFF09\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn m-btn--block m-btn--accent", children: "\u73B0\u5728\u53BB\u6807\u5B9A" }),
      /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn m-btn--block", children: "\u79FB\u9664\u8FD9\u5F20\u80CC\u666F\uFF08\u6539\u7528\u7EAF\u8272\uFF09" }),
      /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn m-btn--block m-btn--ghost", children: "\u6682\u65F6\u7528\u5C45\u4E2D\u663E\u793A\u7EE7\u7EED" })
    ] }) }) : null,
    backendMode ? /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("div", { className: "mg__backend", children: /* @__PURE__ */ (0, import_jsx_runtime33.jsxs)("div", { className: "mg__backend-inner", children: [
      /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("h2", { children: "\u6211\u7684\u4E16\u754C\u4E3B\u6218\u573A" }),
      /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("p", { children: "Galgame \u754C\u9762\u5DF2\u5173\u95ED\u3002WAB \u73B0\u5728\u53EA\u4F5C\u4E3A\u6211\u7684\u4E16\u754C\u7684 AI \u4E0E\u901A\u8BAF\u540E\u7AEF\u8FD0\u884C\uFF1B\u4F60\u5728 WAB \u6216\u5047\u5FAE\u4FE1\u91CC\u8BF4\u7684\u8BDD\u3001\u4E0B\u7684\u547D\u4EE4\uFF0C\u6E38\u620F\u91CC\u7684\u5973\u4EC6\u4F9D\u7136\u4F1A\u54CD\u5E94\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime33.jsx)("button", { type: "button", className: "m-btn", onClick: () => navigate("/phone"), children: "\u6253\u5F00\u624B\u673A" })
    ] }) }) : null
  ] });
}

// src/screens/main/phone.tsx
var import_react25 = __toESM(require_react(), 1);
var import_jsx_runtime34 = __toESM(require_jsx_runtime(), 1);
var PAGE_ONE_APPS = [
  { key: "wechat", glyph: "\u5FAE\u4FE1", label: "\u5FAE\u4FE1", sub: "\u5FAE\u4FE1 \xB7 \u672C\u5730", tone: "warm", to: "/chat" },
  { key: "roster", glyph: "\u89D2\u8272\u5E93", label: "\u89D2\u8272\u5E93", sub: "\u4EBA\u8BBE \xB7 \u6539\u5199", tone: "sand", to: "/roster" },
  { key: "map", glyph: "\u5730\u56FE", label: "\u5730\u56FE", sub: "\u53BB\u5904 \xB7 \u4F4D\u7F6E", tone: "cool", to: "/map" },
  { key: "outfit", glyph: "\u6362\u88C5", label: "\u6362\u88C5", sub: "\u7ACB\u7ED8 \xB7 \u670D\u9970", tone: "warm", to: "/outfit" },
  { key: "qq", glyph: "QQ", label: "QQ", sub: "QQ \xB7 \u8BBA\u575B", tone: "cool", to: "/qqchat" },
  { key: "gallery", glyph: "\u56DE\u5FC6\u5F55", label: "\u56DE\u5FC6\u5F55", sub: "\u76F8\u518C \xB7 \u65E5\u8BB0", tone: "cool", to: "/gallery" },
  { key: "materials", glyph: "\u7D20\u6750\u5E93", label: "\u7D20\u6750\u5E93", sub: "\u80CC\u666F \xB7 \u97F3\u4E50", tone: "sand", to: "/materials" },
  { key: "workshop", glyph: "\u5DE5\u574A", label: "\u5DE5\u574A", sub: "\u573A\u666F \xB7 \u6A21\u5F0F", tone: "sand", to: "/scenelibrary" },
  { key: "worldbook", glyph: "\u4E16\u754C\u4E66", label: "\u4E16\u754C\u4E66", sub: "\u8BBE\u5B9A \xB7 \u6761\u76EE", tone: "deep", to: "/worldbook" }
];
var PAGE_TWO_APPS = [
  { key: "forcecast", glyph: "\u70B9\u540D", label: "\u70B9\u540D", sub: "\u5F3A\u5236\u5165\u620F", tone: "warm", to: "/forcecast" },
  { key: "shop", glyph: "\u5546\u5E97", label: "\u5546\u5E97", sub: "\u793C\u7269 \xB7 \u9053\u5177", tone: "warm", to: "/shop" },
  { key: "gobang", glyph: "\u4E94\u5B50\u68CB", label: "\u4E94\u5B50\u68CB", sub: "\u4E94\u8FDE", tone: "sand", to: "/game?kind=gobang", small: true },
  { key: "animalchess", glyph: "\u6597\u517D\u68CB", label: "\u6597\u517D\u68CB", sub: "\u517D\u68CB", tone: "sand", to: "/game?kind=animalchess", small: true },
  { key: "ludo", glyph: "\u98DE\u884C\u68CB", label: "\u98DE\u884C\u68CB", sub: "\u56DB\u4EBA", tone: "cool", to: "/game?kind=ludo", small: true },
  { key: "chess", glyph: "\u56FD\u9645\u8C61\u68CB", label: "\u56FD\u9645\u8C61\u68CB", sub: "64 \u683C", tone: "cool", to: "/game?kind=chess", small: true },
  { key: "chinesechess", glyph: "\u4E2D\u56FD\u8C61\u68CB", label: "\u4E2D\u56FD\u8C61\u68CB", sub: "\u6CB3\u754C", tone: "warm", to: "/game?kind=chinesechess", small: true },
  { key: "snake", glyph: "\u8D2A\u5403\u86C7", label: "\u8D2A\u5403\u86C7", sub: "\u8D8A\u5403\u8D8A\u957F", tone: "sand", to: "/game?kind=snake", small: true },
  { key: "go", glyph: "\u56F4\u68CB", label: "\u56F4\u68CB", sub: "19 \u8DEF", tone: "deep", to: "/game?kind=go", small: true }
];
var DOCK_APPS = [
  { key: "dock-wechat", glyph: "\u5FAE\u4FE1", label: "\u5FAE\u4FE1", sub: "", tone: "warm", to: "/chat", small: true },
  { key: "dock-roster", glyph: "\u89D2\u8272\u5E93", label: "\u89D2\u8272\u5E93", sub: "", tone: "sand", to: "/roster", small: true },
  { key: "dock-gallery", glyph: "\u56DE\u5FC6\u5F55", label: "\u56DE\u5FC6\u5F55", sub: "", tone: "cool", to: "/gallery", small: true },
  { key: "dock-materials", glyph: "\u7D20\u6750\u5E93", label: "\u7D20\u6750\u5E93", sub: "", tone: "sand", to: "/materials", small: true }
];
function periodLabel(hour) {
  if (hour >= 5 && hour < 11) return "\u4E0A\u5348";
  if (hour >= 11 && hour < 13) return "\u4E2D\u5348";
  if (hour >= 13 && hour < 17) return "\u4E0B\u5348";
  if (hour >= 17 && hour < 19) return "\u508D\u665A";
  return "\u665A\u4E0A";
}
function pad(value) {
  return String(value).padStart(2, "0");
}
function Phone({ navigate, back }) {
  const [page, setPage] = (0, import_react25.useState)(0);
  const now = /* @__PURE__ */ new Date();
  const clock = `${pad(now.getHours())}:${pad(now.getMinutes())}`;
  const statusTime = `${pad(now.getMonth() + 1)}-${pad(now.getDate())} ${periodLabel(now.getHours())}`;
  const appButton = (tile, className = "") => /* @__PURE__ */ (0, import_jsx_runtime34.jsxs)(
    "button",
    {
      type: "button",
      className: `mp__app ${className}`.trim(),
      onClick: () => navigate(tile.to),
      children: [
        /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("span", { className: `mp__tile${tile.small ? " mp__tile--sm" : ""}`, "data-tone": tile.tone, children: /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("span", { children: tile.glyph }) }),
        tile.sub ? /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("span", { className: "mp__app-label", children: tile.sub }) : null
      ]
    },
    tile.key
  );
  return /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("div", { className: "mp", children: /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("div", { className: "mp__body", children: /* @__PURE__ */ (0, import_jsx_runtime34.jsxs)("div", { className: "mp__screen", children: [
    /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("span", { className: "mp__glow mp__glow--a", "aria-hidden": "true" }),
    /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("span", { className: "mp__glow mp__glow--b", "aria-hidden": "true" }),
    /* @__PURE__ */ (0, import_jsx_runtime34.jsxs)("div", { className: "mp__status", children: [
      /* @__PURE__ */ (0, import_jsx_runtime34.jsxs)("button", { type: "button", className: "mp__back-pill", onClick: back, children: [
        /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("span", { children: "\u2039" }),
        /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("span", { children: "\u6E38\u620F" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("span", { className: "mp__time", children: clock }),
      /* @__PURE__ */ (0, import_jsx_runtime34.jsxs)("div", { className: "mp__signals", "aria-label": "\u4FE1\u53F7 \u6EE1\u683C", children: [
        /* @__PURE__ */ (0, import_jsx_runtime34.jsxs)("span", { className: "mp__bars", "aria-hidden": "true", children: [
          /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("i", { style: { height: 3.5 } }),
          /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("i", { style: { height: 5.5 } }),
          /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("i", { style: { height: 7.5 } }),
          /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("i", { style: { height: 9.5 } })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("span", { className: "mp__wifi", children: "WiFi" }),
        /* @__PURE__ */ (0, import_jsx_runtime34.jsxs)("span", { className: "mp__battery", "aria-hidden": "true", children: [
          /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("span", { className: "mp__battery-frame", children: /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("i", {}) }),
          /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("span", { className: "mp__battery-tip" })
        ] })
      ] })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("div", { className: "mp__pages", children: page === 0 ? /* @__PURE__ */ (0, import_jsx_runtime34.jsxs)("div", { className: "mp__page", children: [
      /* @__PURE__ */ (0, import_jsx_runtime34.jsxs)("div", { className: "mp__clock", children: [
        /* @__PURE__ */ (0, import_jsx_runtime34.jsxs)("div", { children: [
          /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("h2", { children: clock }),
          /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("div", { className: "mp__clock-sub", children: statusTime }),
          /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("div", { className: "mp__clock-line", children: "\u6E29\u6696\u5982\u521D \xB7 \u624B\u673A" })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("span", { className: "mp__weather", children: "\u6674\u5929" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("span", { className: "mp__sect", children: "TA \u7684\u4E16\u754C" }),
      /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("div", { className: "mp__grid", children: PAGE_ONE_APPS.slice(0, 6).map((tile) => appButton(tile)) }),
      /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("span", { className: "mp__sect", children: "\u5185\u5BB9\u4E0E\u8BBE\u5B9A" }),
      /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("div", { className: "mp__grid", children: PAGE_ONE_APPS.slice(6).map((tile) => appButton(tile)) })
    ] }) : /* @__PURE__ */ (0, import_jsx_runtime34.jsxs)("div", { className: "mp__page", children: [
      /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("span", { className: "mp__sect", children: "\u4E00\u8D77\u73A9" }),
      /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("div", { className: "mp__grid", children: PAGE_TWO_APPS.slice(0, 2).map((tile) => appButton(tile)) }),
      /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("span", { className: "mp__sect", children: "\u5C0F\u6E38\u620F" }),
      /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("div", { className: "mp__grid", children: PAGE_TWO_APPS.slice(2).map((tile) => appButton(tile)) })
    ] }) }),
    /* @__PURE__ */ (0, import_jsx_runtime34.jsxs)("div", { className: "mp__dots", children: [
      /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("button", { type: "button", className: "mp__dot", "aria-current": page === 0, "aria-label": "\u7B2C\u4E00\u9875", onClick: () => setPage(0), children: /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("i", {}) }),
      /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("button", { type: "button", className: "mp__dot", "aria-current": page === 1, "aria-label": "\u7B2C\u4E8C\u9875", onClick: () => setPage(1), children: /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("i", {}) })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("div", { className: "mp__dock", children: DOCK_APPS.map((tile) => appButton(tile)) }),
    /* @__PURE__ */ (0, import_jsx_runtime34.jsxs)("div", { className: "mp__nav", children: [
      /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("button", { type: "button", "data-k": "back", onClick: back, "aria-label": "\u56DE\u9000", children: "\u25C1" }),
      /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("button", { type: "button", "data-k": "home", onClick: () => setPage(0), "aria-label": "\u56DE\u5230\u684C\u9762", children: "\u25A1" }),
      /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("button", { type: "button", "data-k": "settings", onClick: () => navigate("/settings"), "aria-label": "\u8BBE\u7F6E", children: "\u2261" }),
      /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("button", { type: "button", className: "mp__nav-tail", onClick: () => navigate("/main"), children: "\u8F6C\u5230\u4E3B\u7A97\u53E3" })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("div", { className: "mp__homebar", "aria-hidden": "true" }),
    /* @__PURE__ */ (0, import_jsx_runtime34.jsx)("span", { className: "mp__speaker", "aria-hidden": "true" })
  ] }) }) });
}

// src/screens/main/select.tsx
var import_react26 = __toESM(require_react(), 1);
var import_jsx_runtime35 = __toESM(require_jsx_runtime(), 1);
function pad2(value) {
  return String(value).padStart(2, "0");
}
function savesLabel(savedAt) {
  const at = new Date(savedAt);
  if (Number.isNaN(at.getTime())) return "\u8FD8\u6CA1\u6709\u5B58\u6863";
  return `\u5DF2\u6709 1 \u4E2A\u5B58\u6863 \xB7 \u6700\u8FD1 ${pad2(at.getMonth() + 1)}-${pad2(at.getDate())} ${pad2(at.getHours())}:${pad2(at.getMinutes())}`;
}
function Select2({ bridge: bridge2, navigate, back }) {
  const [cards, setCards] = (0, import_react26.useState)([]);
  const [addOpen, setAddOpen] = (0, import_react26.useState)(false);
  (0, import_react26.useEffect)(() => {
    let alive = true;
    void bridge2.request("settings.get", {}).then((result) => {
      const save = result.save;
      if (!alive || !save) return;
      setCards([
        {
          id: save.character,
          name: save.character,
          subtitle: "\u70B9\u51FB\u5F00\u59CB\u804A\u5929",
          savesLabel: savesLabel(save.savedAt)
        }
      ]);
    }).catch(() => void 0);
    return () => {
      alive = false;
    };
  }, [bridge2]);
  return /* @__PURE__ */ (0, import_jsx_runtime35.jsxs)("div", { className: "mc", children: [
    /* @__PURE__ */ (0, import_jsx_runtime35.jsxs)("div", { className: "mc__head", children: [
      /* @__PURE__ */ (0, import_jsx_runtime35.jsx)("button", { type: "button", className: "mc__back", onClick: back, "aria-label": "\u8FD4\u56DE", children: "\u2039" }),
      /* @__PURE__ */ (0, import_jsx_runtime35.jsx)("h1", { className: "mc__title", "data-page-title": true, tabIndex: -1, children: "\u5FAE\u4FE1" }),
      /* @__PURE__ */ (0, import_jsx_runtime35.jsx)(
        "button",
        {
          type: "button",
          className: "mc__plus",
          onClick: () => setAddOpen((open) => !open),
          "aria-label": "\u65B0\u5EFA",
          children: "\uFF0B"
        }
      ),
      addOpen ? /* @__PURE__ */ (0, import_jsx_runtime35.jsxs)("div", { className: "mc__addmenu", children: [
        /* @__PURE__ */ (0, import_jsx_runtime35.jsx)("button", { type: "button", className: "m-btn m-btn--block", onClick: () => navigate("/studio?new=1&start=1"), children: "\u624B\u52A8\u65B0\u5EFA" }),
        /* @__PURE__ */ (0, import_jsx_runtime35.jsx)("button", { type: "button", className: "m-btn m-btn--block", onClick: () => navigate("/studio?new=1&start=1"), children: "AI \u9020\u89D2\u8272" })
      ] }) : null
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime35.jsx)("div", { className: "mc__sep" }),
    /* @__PURE__ */ (0, import_jsx_runtime35.jsx)("div", { className: "mc__list", children: cards.length === 0 ? /* @__PURE__ */ (0, import_jsx_runtime35.jsxs)("div", { className: "mc__empty", children: [
      /* @__PURE__ */ (0, import_jsx_runtime35.jsx)("span", { className: "mc__empty-mark", children: "\u5FAE\u4FE1" }),
      /* @__PURE__ */ (0, import_jsx_runtime35.jsx)("span", { className: "mc__empty-title", children: "\u8FD8\u6CA1\u6709\u5FAE\u4FE1\u597D\u53CB" }),
      /* @__PURE__ */ (0, import_jsx_runtime35.jsx)("span", { className: "mc__empty-hint", children: "\u8BA9 AI \u7528\u300Cadd_wechat\u300D\u628A\u5267\u60C5\u91CC\u7684\u89D2\u8272\u52A0\u4E3A\u597D\u53CB\uFF0C\u6216\u4E0B\u65B9\u65B0\u5EFA/\u5BFC\u5165" })
    ] }) : cards.map((card) => /* @__PURE__ */ (0, import_jsx_runtime35.jsxs)("div", { children: [
      /* @__PURE__ */ (0, import_jsx_runtime35.jsxs)(
        "button",
        {
          type: "button",
          className: "mc__row",
          onClick: () => navigate(`/newrun?character=${encodeURIComponent(card.id)}`),
          children: [
            /* @__PURE__ */ (0, import_jsx_runtime35.jsx)("span", { className: "mc__avatar", children: card.name.slice(0, 1) }),
            /* @__PURE__ */ (0, import_jsx_runtime35.jsxs)("span", { className: "mc__row-text", children: [
              /* @__PURE__ */ (0, import_jsx_runtime35.jsx)("span", { className: "mc__row-name", children: card.name }),
              /* @__PURE__ */ (0, import_jsx_runtime35.jsx)("span", { className: "mc__row-sub", children: card.subtitle })
            ] }),
            /* @__PURE__ */ (0, import_jsx_runtime35.jsx)("span", { className: "mc__row-saves", children: card.savesLabel })
          ]
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime35.jsx)("div", { className: "mc__row-sep" })
    ] }, card.id)) }),
    /* @__PURE__ */ (0, import_jsx_runtime35.jsxs)("div", { className: "mc__foot", children: [
      /* @__PURE__ */ (0, import_jsx_runtime35.jsx)("button", { type: "button", className: "m-btn m-btn--accent", onClick: () => navigate("/studio?new=1&start=1"), children: "\uFF0B \u65B0\u5EFA\u89D2\u8272" }),
      /* @__PURE__ */ (0, import_jsx_runtime35.jsx)("button", { type: "button", className: "m-btn m-btn--soft", children: "\u5BFC\u5165\u89D2\u8272\u5305" })
    ] })
  ] });
}

// src/screens/main/setup.tsx
var import_react27 = __toESM(require_react(), 1);
var import_jsx_runtime36 = __toESM(require_jsx_runtime(), 1);
var LANGS = [
  { value: "zh-CN", label: "\u7B80\u4F53\u4E2D\u6587" },
  { value: "zh-TW", label: "\u7E41\u9AD4\u4E2D\u6587" },
  { value: "en-US", label: "English" },
  { value: "ja-JP", label: "\u65E5\u672C\u8A9E" }
];
var PETS = [
  { value: "0", label: "\u4E0D\u4E3B\u52A8\u51FA\u73B0" },
  { value: "5", label: "\u7A7A\u95F2 5 \u5206\u949F\u540E" },
  { value: "15", label: "\u7A7A\u95F2 15 \u5206\u949F\u540E" },
  { value: "30", label: "\u7A7A\u95F2 30 \u5206\u949F\u540E" }
];
var STEP_COUNT = 4;
function Setup({ navigate }) {
  const [step, setStep] = (0, import_react27.useState)(0);
  const [lang, setLang] = (0, import_react27.useState)("zh-CN");
  const [topmost, setTopmost] = (0, import_react27.useState)(false);
  const [pet, setPet] = (0, import_react27.useState)("0");
  const [aiUrl, setAiUrl] = (0, import_react27.useState)("");
  const [aiKey, setAiKey] = (0, import_react27.useState)("");
  const [aiModel, setAiModel] = (0, import_react27.useState)("");
  const leave = () => navigate("/");
  const next = () => {
    if (step < 3) setStep(step + 1);
    else navigate("/select");
  };
  const option = (key, label, selected, pick) => /* @__PURE__ */ (0, import_jsx_runtime36.jsxs)(
    "button",
    {
      type: "button",
      className: "msu__opt",
      role: "radio",
      "aria-checked": selected,
      onClick: pick,
      children: [
        /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("span", { className: "msu__opt-bar", "aria-hidden": "true" }),
        /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("span", { className: "msu__opt-label", children: label }),
        /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("span", {})
      ]
    },
    key
  );
  return /* @__PURE__ */ (0, import_jsx_runtime36.jsxs)("div", { className: "msu", children: [
    /* @__PURE__ */ (0, import_jsx_runtime36.jsxs)(Glass, { as: "div", tone: "card", radius: 12, className: "msu__card", children: [
      /* @__PURE__ */ (0, import_jsx_runtime36.jsxs)("div", { className: "msu__head", children: [
        /* @__PURE__ */ (0, import_jsx_runtime36.jsx)(
          "button",
          {
            type: "button",
            className: "m-btn m-btn--ghost msu__arrow",
            onClick: () => setStep(step - 1),
            style: { visibility: step > 0 ? "visible" : "hidden" },
            "aria-label": "\u8FD4\u56DE",
            children: "\u2190"
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("span", {}),
        /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("span", { className: "msu__eyebrow", children: `${step + 1} / ${STEP_COUNT}` })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime36.jsxs)("div", { className: "msu__step", children: [
        step === 0 ? /* @__PURE__ */ (0, import_jsx_runtime36.jsxs)(import_jsx_runtime36.Fragment, { children: [
          /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("h2", { className: "msu__title", children: "\u5148\u9009\u4E00\u79CD\u8BED\u8A00" }),
          /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("p", { className: "msu__sub", children: "\u70B9\u4E86\u7ACB\u523B\u6362\uFF0C\u6574\u4E2A\u8F6F\u4EF6\u8DDF\u7740\u53D8\uFF0C\u4E0D\u7528\u91CD\u542F\u3002" }),
          /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("div", { className: "msu__opts", children: LANGS.map((item) => option(item.value, item.label, item.value === lang, () => setLang(item.value))) })
        ] }) : null,
        step === 1 ? /* @__PURE__ */ (0, import_jsx_runtime36.jsxs)(import_jsx_runtime36.Fragment, { children: [
          /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("h2", { className: "msu__title", children: "\u8BA9\u5B83\u5E38\u5728" }),
          /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("p", { className: "msu__sub", children: "\u8FD9\u4E24\u9879\u53EA\u7BA1 Windows \u4E0A\u7684\u5B83\uFF0C\u4E4B\u540E\u5728\u8BBE\u7F6E\u91CC\u968F\u65F6\u6539\u56DE\u6765\u3002" }),
          /* @__PURE__ */ (0, import_jsx_runtime36.jsxs)("div", { className: "msu__toggle-row", children: [
            /* @__PURE__ */ (0, import_jsx_runtime36.jsxs)("span", { className: "msu__toggle-text", children: [
              /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("b", { children: "\u7A97\u53E3\u7F6E\u9876" }),
              /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("span", { children: "\u7559\u5728\u6700\u524D\u9762\uFF0C\u4E0D\u88AB\u522B\u7684\u7A97\u53E3\u76D6\u4F4F" })
            ] }),
            /* @__PURE__ */ (0, import_jsx_runtime36.jsx)(GlassSwitch, { label: "\u7A97\u53E3\u7F6E\u9876", checked: topmost, onChange: setTopmost })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("div", { className: "mc__sep" }),
          /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("span", { className: "msu__field-label", children: "\u684C\u5BA0\u81EA\u5DF1\u51FA\u73B0\u7684\u95F4\u9694" }),
          /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("div", { className: "msu__opts", children: PETS.map((item) => option(item.value, item.label, item.value === pet, () => setPet(item.value))) })
        ] }) : null,
        step === 2 ? /* @__PURE__ */ (0, import_jsx_runtime36.jsxs)(import_jsx_runtime36.Fragment, { children: [
          /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("h2", { className: "msu__title", children: "\u63A5\u4E0A\u4F1A\u8BF4\u8BDD\u7684\u90A3\u90E8\u5206" }),
          /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("p", { className: "msu__sub", children: "\u586B\u4E86\u624D\u6709\u771F\u5B9E\u56DE\u590D\u3002\u5148\u8DF3\u8FC7\u4E5F\u884C\uFF0C\u4EBA\u7269\u548C\u9875\u9762\u7167\u5E38\u7528\u3002" }),
          /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("span", { className: "msu__field-label", children: "API \u5730\u5740" }),
          /* @__PURE__ */ (0, import_jsx_runtime36.jsx)(
            "input",
            {
              className: "msu__entry",
              value: aiUrl,
              placeholder: "https://api.example.com/v1/chat/completions",
              "aria-label": "API \u5730\u5740",
              onChange: (event) => setAiUrl(event.target.value)
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("span", { className: "msu__line" }),
          /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("span", { className: "msu__field-label", children: "API \u5BC6\u94A5" }),
          /* @__PURE__ */ (0, import_jsx_runtime36.jsx)(
            "input",
            {
              className: "msu__entry",
              type: "password",
              value: aiKey,
              placeholder: "sk-\u2026",
              "aria-label": "API \u5BC6\u94A5",
              onChange: (event) => setAiKey(event.target.value)
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("span", { className: "msu__line" }),
          /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("span", { className: "msu__field-label", children: "\u6A21\u578B" }),
          /* @__PURE__ */ (0, import_jsx_runtime36.jsx)(
            "input",
            {
              className: "msu__entry",
              value: aiModel,
              placeholder: "gpt-4o",
              "aria-label": "\u6A21\u578B",
              onChange: (event) => setAiModel(event.target.value)
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("span", { className: "msu__line" }),
          /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("p", { className: "msu__hint", children: "\u5BC6\u94A5\u53EA\u53D1\u7ED9\u4F60\u586B\u7684\u8FD9\u4E2A\u5730\u5740\uFF0C\u4E0D\u8FDB\u65E5\u5FD7\uFF0C\u4E5F\u4E0D\u8D70\u522B\u5904\u3002" })
        ] }) : null,
        step === 3 ? /* @__PURE__ */ (0, import_jsx_runtime36.jsxs)(import_jsx_runtime36.Fragment, { children: [
          /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("h2", { className: "msu__title", children: "\u5C31\u8FD9\u4E9B" }),
          /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("p", { className: "msu__sub", children: "\u5176\u4F59\u7684\u8FB9\u7528\u8FB9\u8C03\u3002\u73B0\u5728\u53BB\u8BA4\u8BC6\u7B2C\u4E00\u4E2A\u4EBA\uFF1F" }),
          /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("span", { className: "msu__teach-head", children: "\u5F00\u59CB\u4E4B\u524D" }),
          /* @__PURE__ */ (0, import_jsx_runtime36.jsxs)("div", { className: "msu__teach", children: [
            /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("span", { className: "msu__teach-dot", "aria-hidden": "true" }),
            /* @__PURE__ */ (0, import_jsx_runtime36.jsxs)("span", { children: [
              /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("b", { children: "\u5148\u6709\u4E00\u4F4D\u89D2\u8272" }),
              /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("span", { children: "\u65B0\u5EFA\u5C31\u591F\uFF1A\u540D\u5B57\u3001\u6027\u522B\u3001\u6027\u683C\uFF0C\u4E0D\u7528\u7ACB\u7ED8\uFF1B\u4E5F\u53EF\u5BFC\u5165\u89D2\u8272\u5305\uFF08.zip\uFF09" })
            ] })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime36.jsxs)("div", { className: "msu__teach", children: [
            /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("span", { className: "msu__teach-dot", "aria-hidden": "true" }),
            /* @__PURE__ */ (0, import_jsx_runtime36.jsxs)("span", { children: [
              /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("b", { children: "\u771F\u5B9E\u56DE\u590D\u8981\u63A5 AI" }),
              /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("span", { children: "\u8BBE\u7F6E \u2192\u300CAI \u4E0E\u6A21\u578B\u300D\uFF1A\u586B\u5730\u5740\u3001\u5BC6\u94A5\u3001\u6A21\u578B" })
            ] })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime36.jsxs)("div", { className: "msu__teach", children: [
            /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("span", { className: "msu__teach-dot", "aria-hidden": "true" }),
            /* @__PURE__ */ (0, import_jsx_runtime36.jsxs)("span", { children: [
              /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("b", { children: "\u968F\u65F6\u518D\u8C03" }),
              /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("span", { children: "\u5916\u89C2\u4E0E\u4E3B\u9898 \u6362\u98CE\u683C\u548C\u914D\u8272\uFF1B\u6570\u636E\u4E0E\u7D20\u6750 \u91CC\u5BFC\u5165\u5BFC\u51FA\u7D20\u6750\u5305" })
            ] })
          ] })
        ] }) : null
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime36.jsxs)("div", { className: "msu__actions", children: [
        step > 0 ? /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("button", { type: "button", className: "m-btn", onClick: () => setStep(step - 1), children: "\u8FD4\u56DE" }) : null,
        step === 2 ? /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("button", { type: "button", className: "m-btn", onClick: next, children: "\u8DF3\u8FC7\u8FD9\u6B65" }) : null,
        step === 3 ? /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("button", { type: "button", className: "m-btn", onClick: leave, children: "\u4EE5\u540E\u518D\u8BF4" }) : null,
        step === 3 ? /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("button", { type: "button", className: "m-btn m-btn--primary", onClick: () => navigate("/select"), children: "\u53BB\u521B\u5EFA\u89D2\u8272" }) : /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("button", { type: "button", className: "m-btn m-btn--primary", onClick: next, children: "\u4E0B\u4E00\u6B65" })
      ] })
    ] }),
    step === 3 ? null : /* @__PURE__ */ (0, import_jsx_runtime36.jsx)("button", { type: "button", className: "m-btn msu__skip", onClick: leave, children: "\u8DF3\u8FC7\u914D\u7F6E" })
  ] });
}

// src/screens/main/index.tsx
var screens4 = {
  main: Main,
  phone: Phone,
  select: Select2,
  setup: Setup
};

// src/screens/save-chat/bindwizard.tsx
var import_react28 = __toESM(require_react(), 1);
var import_jsx_runtime37 = __toESM(require_jsx_runtime(), 1);
var SLOTS2 = [
  { id: "shenyan", title: "\u6C88\u781A", desc: "\u89D2\u8272 \u6C88\u781A \xB7 11-24 21:07", enabled: true }
];
var CHARACTERS = [
  { id: "shenyan", title: "\u6C88\u781A", desc: "\u6027\u683C\u672A\u77E5", enabled: true },
  { id: "xiaoyu", title: "\u5C0F\u96E8", desc: "\u6027\u683C\u672A\u77E5", enabled: true }
];
var WORLD = "Minecraft \u4E16\u754C";
var OWNER = "\u672A\u77E5";
var MAIDS = [];
function Bindwizard({ back }) {
  const [saveId, setSaveId] = (0, import_react28.useState)("shenyan");
  const [newSave, setNewSave] = (0, import_react28.useState)(false);
  const [charId, setCharId] = (0, import_react28.useState)("shenyan");
  const [fromMaid, setFromMaid] = (0, import_react28.useState)(false);
  const hasMaid = MAIDS.length > 0;
  const hasChar = newSave ? fromMaid || charId.length > 0 : SLOTS2.some((slot) => slot.id === saveId);
  const hint = !hasMaid ? "\u6E38\u620F\u91CC\u8FD8\u6CA1\u6709\u53EF\u7ED1\u5B9A\u7684\u5973\u4EC6" : !hasChar ? "\u5148\u9009\u4E00\u4EFD\u5B58\u6863\uFF0C\u6216\u4E3A\u8FD9\u4EFD\u65B0\u6863\u9009\u4E00\u4E2A\u89D2\u8272" : "";
  const pick = (item, selected, onTap) => /* @__PURE__ */ (0, import_jsx_runtime37.jsxs)(
    "button",
    {
      type: "button",
      className: "sc-pick",
      "data-selected": selected,
      "data-enabled": item.enabled,
      onClick: item.enabled ? onTap : void 0,
      children: [
        /* @__PURE__ */ (0, import_jsx_runtime37.jsx)("span", { className: "sc-pick__bar", style: { visibility: selected ? "visible" : "hidden" } }),
        /* @__PURE__ */ (0, import_jsx_runtime37.jsxs)("span", { className: "sc-pick__text", children: [
          /* @__PURE__ */ (0, import_jsx_runtime37.jsx)("span", { className: "sc-pick__title", children: item.title }),
          item.desc ? /* @__PURE__ */ (0, import_jsx_runtime37.jsx)("span", { className: "sc-pick__desc", children: item.desc }) : null
        ] })
      ]
    },
    item.id
  );
  return /* @__PURE__ */ (0, import_jsx_runtime37.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime37.jsx)(
      PageHeader,
      {
        title: "\u63A5\u5165 Minecraft \u4E16\u754C",
        subtitle: `${WORLD} \xB7 \u4E3B\u4EBA ${OWNER} \xB7 ${MAIDS.length} \u53EA\u5973\u4EC6`,
        onBack: back
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime37.jsxs)("div", { className: "screen sc-wiz", children: [
      /* @__PURE__ */ (0, import_jsx_runtime37.jsx)(Card, { children: /* @__PURE__ */ (0, import_jsx_runtime37.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-1)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime37.jsxs)("span", { className: "sc-line", children: [
          "\u4E16\u754C",
          /* @__PURE__ */ (0, import_jsx_runtime37.jsx)("b", { className: "sc-line__strong", children: WORLD })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime37.jsxs)("span", { className: "sc-line", children: [
          "\u4E3B\u4EBA",
          /* @__PURE__ */ (0, import_jsx_runtime37.jsx)("b", { children: OWNER })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime37.jsxs)("span", { className: "sc-line", children: [
          "\u5973\u4EC6",
          /* @__PURE__ */ (0, import_jsx_runtime37.jsx)("b", { children: MAIDS.length === 0 ? "\u8FD8\u6CA1\u770B\u5230\u5973\u4EC6" : MAIDS.join("\u3001") })
        ] })
      ] }) }),
      /* @__PURE__ */ (0, import_jsx_runtime37.jsx)(
        Card,
        {
          title: "\u5B58\u6863",
          note: "\u9009\u4E00\u4EFD\u5DF2\u6709\u5B58\u6863\u63A5\u7740\u7528\uFF0C\u6216\u8005\u4E3A\u8FD9\u4E2A\u4E16\u754C\u65B0\u5EFA\u4E00\u4EFD\u3002\u9009\u300C\u65B0\u5EFA\u300D\u5E76\u70B9\u53F3\u4E0B\u89D2\u6309\u94AE\uFF0C\u4F1A\u6253\u5F00 WAB \u81EA\u5DF1\u7684\u65B0\u5EFA\u5B58\u6863\u5411\u5BFC\uFF08\u4E16\u754C \u2192 \u4EBA\u8BBE \u2192 \u7EC6\u5219 \u2192 \u5F00\u59CB\uFF09\uFF0C\u8D70\u5B8C\u81EA\u52A8\u56DE\u6765\u5B8C\u6210\u7ED1\u5B9A\u3002",
          children: /* @__PURE__ */ (0, import_jsx_runtime37.jsxs)("div", { className: "sc-picks", children: [
            SLOTS2.map(
              (slot) => pick(slot, !newSave && saveId === slot.id, () => {
                setNewSave(false);
                setSaveId(slot.id);
              })
            ),
            pick(
              { id: "new", title: "\u65B0\u5EFA\u4E00\u4EFD\u5B58\u6863", desc: `\u4E3A\u300C${WORLD}\u300D\u5F00\u4E00\u4EFD\u65B0\u6863\uFF0C\u5E76\u7ED1\u5B9A\u4E0B\u9762\u9009\u7684\u89D2\u8272`, enabled: true },
              newSave,
              () => {
                setNewSave(true);
                setSaveId("");
              }
            )
          ] })
        }
      ),
      newSave ? /* @__PURE__ */ (0, import_jsx_runtime37.jsx)(
        Card,
        {
          title: "\u89D2\u8272",
          note: "\u65B0\u5EFA\u7684\u5B58\u6863\u8981\u7ED1\u5B9A\u4E00\u4E2A\u89D2\u8272\uFF1A\u5979\u5728 WAB \u91CC\u7531\u8FD9\u4E2A\u89D2\u8272\u8BF4\u8BDD\u3001\u8BB0\u4E8B\u3002\u5148\u5728\u8FD9\u91CC\u5B9A\u597D\u4EBA\uFF0C\u518D\u4EA4\u7ED9\u65B0\u5EFA\u5B58\u6863\u5411\u5BFC\u3002",
          children: /* @__PURE__ */ (0, import_jsx_runtime37.jsxs)("div", { className: "sc-picks", children: [
            CHARACTERS.map(
              (character) => pick(character, !fromMaid && charId === character.id, () => {
                setFromMaid(false);
                setCharId(character.id);
              })
            ),
            pick(
              {
                id: "from-maid",
                title: "\u7528\u300C\u5973\u4EC6\u300D\u7684\u540D\u5B57\u65B0\u5EFA\u89D2\u8272",
                desc: "\u6309\u5973\u4EC6\u540D\u5EFA\u4E00\u4E2A\u9ED8\u8BA4\u89D2\u8272\uFF08\u6E29\u67D4\u53EF\u7231\uFF09\uFF0C\u4E4B\u540E\u53EF\u4EE5\u5728\u89D2\u8272\u5E93\u91CC\u6162\u6162\u6539",
                enabled: true
              },
              fromMaid,
              () => setFromMaid(true)
            )
          ] })
        }
      ) : null,
      /* @__PURE__ */ (0, import_jsx_runtime37.jsx)(Card, { title: "\u5973\u4EC6", note: "\u9009\u62E9\u4E00\u4E2A\u8981\u63A5\u8FDB\u8FD9\u4E2A\u4E16\u754C\u7684\u5973\u4EC6\uFF1B\u5176\u4F59\u5973\u4EC6\u4E4B\u540E\u53EF\u4EE5\u5728\u63D2\u4EF6\u91CC\u9010\u4E2A\u7ED1\u5B9A\u3002", children: MAIDS.length === 0 ? /* @__PURE__ */ (0, import_jsx_runtime37.jsx)("p", { className: "cap", children: "\u6E38\u620F\u91CC\u8FD8\u6CA1\u6709\u5973\u4EC6\uFF1A\u5148\u8BAD\u670D\u4E00\u53EA\uFF0C\u518D\u56DE\u6765\u8FDE\u63A5\u3002" }) : /* @__PURE__ */ (0, import_jsx_runtime37.jsx)("div", { className: "sc-picks", children: MAIDS.map((maid) => pick({ id: maid, title: maid, desc: "", enabled: true }, false, () => void 0)) }) })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime37.jsxs)("div", { className: "sc-wiz__footer", children: [
      /* @__PURE__ */ (0, import_jsx_runtime37.jsx)("span", { className: "cap", children: hint }),
      /* @__PURE__ */ (0, import_jsx_runtime37.jsx)("span", { style: hasMaid && hasChar ? void 0 : { opacity: 0.5, pointerEvents: "none" }, "aria-disabled": !(hasMaid && hasChar), children: /* @__PURE__ */ (0, import_jsx_runtime37.jsx)(GlassButton, { size: "sm", children: newSave ? "\u65B0\u5EFA\u5B58\u6863\u5E76\u7ED1\u5B9A" : "\u5F00\u59CB\u7ED1\u5B9A" }) })
    ] })
  ] });
}

// src/screens/save-chat/parts.tsx
var import_react29 = __toESM(require_react(), 1);
var import_jsx_runtime38 = __toESM(require_jsx_runtime(), 1);
var TIERS = ["\u5173\u95ED", "\u68631", "\u68632", "\u68633"];
function ChatShell({ skin, contacts, partner, modeLabel, messages, onBack, onForum }) {
  const wide = useIsWide(900);
  const isWeChat = skin === "wechat";
  const [activeId, setActiveId] = (0, import_react29.useState)(contacts[0]?.id ?? "");
  const [full, setFull] = (0, import_react29.useState)(false);
  const [listOpen, setListOpen] = (0, import_react29.useState)(!isWeChat && !wide);
  const [search, setSearch] = (0, import_react29.useState)("");
  const [input, setInput] = (0, import_react29.useState)("");
  const [stickersOpen, setStickersOpen] = (0, import_react29.useState)(false);
  const [giftOpen, setGiftOpen] = (0, import_react29.useState)(false);
  const [giftMode, setGiftMode] = (0, import_react29.useState)("gift");
  const [thinkOn, setThinkOn] = (0, import_react29.useState)(false);
  const [tier, setTier] = (0, import_react29.useState)(0);
  (0, import_react29.useEffect)(() => {
    if (wide) setListOpen(false);
  }, [wide]);
  const unreadCount = contacts.filter((contact) => contact.unread).length;
  const railLabel = unreadCount > 0 ? String(unreadCount) : "\u6D88\u606F";
  const partnerName = contacts.find((contact) => contact.id === activeId)?.name ?? partner;
  const query = search.trim();
  const rows2 = query ? contacts.filter((contact) => contact.name.includes(query) || contact.last.includes(query)) : contacts;
  const showListColumn = wide && !full;
  const openContact = (id) => {
    setActiveId(id);
    setListOpen(false);
  };
  const listBody = /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)(import_jsx_runtime38.Fragment, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)("div", { className: "sc-listhead", children: [
      /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { className: "sc-search", children: /* @__PURE__ */ (0, import_jsx_runtime38.jsx)(
        "input",
        {
          value: search,
          placeholder: "\u641C\u7D22",
          "aria-label": "\u641C\u7D22",
          onChange: (event) => setSearch(event.target.value)
        }
      ) }),
      /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("button", { type: "button", className: "sc-plus", "aria-label": "\u5237\u65B0\u8054\u7CFB\u4EBA", children: "\uFF0B" })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("div", { className: "sc-list", children: rows2.length === 0 ? isWeChat ? /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)("div", { className: "sc-empty", children: [
      /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { children: query ? `\u6CA1\u6709\u4F1A\u8BDD\u5339\u914D\u300C${query}\u300D` : "\u8FD9\u4E00\u6863\u8FD8\u6CA1\u6709\u89D2\u8272" }),
      /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("small", { children: "\u5B58\u6863\u91CC\u7684\u89D2\u8272\u90FD\u4F1A\u51FA\u73B0\u5728\u8FD9\u91CC\uFF0C\u968F\u65F6\u80FD\u7ED9\u4F60\u53D1\u6D88\u606F" })
    ] }) : /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)("div", { className: "sc-empty", children: [
      /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { children: "\u8FD8\u6CA1\u6709\u597D\u53CB" }),
      /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("small", { children: "\u8BA9 AI \u7528\u300Cadd_wechat\u300D\u628A\u5267\u60C5\u91CC\u7684\u89D2\u8272\u52A0\u4E3A\u597D\u53CB" })
    ] }) : rows2.map((contact) => /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)(
      "button",
      {
        type: "button",
        className: "sc-contact",
        "aria-pressed": contact.id === activeId,
        onClick: () => openContact(contact.id),
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { className: "sc-avatar", children: contact.name.slice(0, 1) }),
          /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)("span", { className: "sc-contact__text", children: [
            /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { className: "sc-contact__name", children: contact.name }),
            /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { className: "sc-contact__last", children: contact.last })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)("span", { className: "sc-contact__side", children: [
            /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { className: "sc-contact__time", children: contact.time }),
            contact.unread ? isWeChat ? /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("i", { className: "sc-dot", "aria-label": "\u672A\u8BFB" }) : /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("i", { className: "sc-badge", "aria-label": "\u672A\u8BFB" }) : null
          ] })
        ]
      },
      contact.id
    )) })
  ] });
  const chatCol = showListColumn ? isWeChat ? 3 : 2 : 1;
  const header = /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)("div", { className: "sc-chead", style: { gridColumn: chatCol }, children: [
    !wide ? /* @__PURE__ */ (0, import_jsx_runtime38.jsx)(
      "button",
      {
        type: "button",
        className: "sc-iconbtn",
        "aria-label": isWeChat ? "\u4F1A\u8BDD\u5217\u8868" : "\u597D\u53CB",
        onClick: () => setListOpen(true),
        children: isWeChat ? "\u5217\u8868" : "\u597D\u53CB"
      }
    ) : null,
    /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("button", { type: "button", className: "sc-iconbtn", "aria-label": "\u8FD4\u56DE", onClick: onBack, children: "<" }),
    /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { className: "sc-avatar", children: partnerName.slice(0, 1) }),
    /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)("span", { className: "sc-chead__name", children: [
      /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { className: "sc-chead__title", children: partnerName }),
      /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { className: "sc-chead__sub", children: modeLabel })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { className: "sc-chead__acts", children: isWeChat ? /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)(import_jsx_runtime38.Fragment, { children: [
      /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("button", { type: "button", className: "sc-pill", onClick: () => setFull((value) => !value), children: full ? "\u8FD4\u56DE" : "\u5168\u5C4F" }),
      full ? null : /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { className: "sc-pill", children: "\u5FAE\u4FE1" })
    ] }) : /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)(import_jsx_runtime38.Fragment, { children: [
      /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { className: "sc-pill", children: "QQ" }),
      /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("button", { type: "button", className: "sc-pill", onClick: onForum, children: "\u8BBA\u575B" })
    ] }) })
  ] });
  const body = /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)("div", { className: "sc-body", style: { gridColumn: chatCol }, children: [
    /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("div", { className: "sc-msgs", children: messages.length === 0 ? isWeChat ? /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)("div", { className: "sc-empty", style: { margin: "auto" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { children: "\u8FD8\u6CA1\u6709\u804A\u8FC7" }),
      /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("small", { children: "\u8BF4\u70B9\u4EC0\u4E48\uFF0C\u5979\u4F1A\u5728\u4E0B\u9762\u56DE\u4F60" })
    ] }) : null : messages.map((message) => /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("div", { className: message.mine ? "sc-msg sc-msg--mine" : "sc-msg", children: /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { className: message.mine ? "sc-bubble sc-bubble--mine" : "sc-bubble sc-bubble--theirs", children: message.text }) }, message.id)) }),
    stickersOpen ? /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)("div", { className: "sc-stickers", children: [
      /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)("div", { className: "sc-stickers__head", children: [
        /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { children: "\u6211\u7684\u8868\u60C5\u5305" }),
        /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { className: "sc-pill", children: "\u5BFC\u5165\u8868\u60C5" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("p", { className: "sc-stickers__empty", children: "\u8FD8\u6CA1\u6709\u8868\u60C5\uFF0C\u70B9\u300C\u5BFC\u5165\u8868\u60C5\u300D\u6DFB\u52A0\uFF08\u56FE\u7247/\u52A8\u56FE/\u89C6\u9891/\u4EFB\u610F\u6587\u4EF6\u5747\u53EF\uFF09" })
    ] }) : null,
    /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)("div", { className: "sc-input", children: [
      /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)("div", { className: "sc-think", children: [
        /* @__PURE__ */ (0, import_jsx_runtime38.jsx)(
          "button",
          {
            type: "button",
            className: "sc-think__toggle",
            onClick: () => {
              setThinkOn((value) => !value);
              setTier((value) => value > 0 ? value : 1);
            },
            children: thinkOn ? "\u601D\u8003\uFF1A\u5F00" : "\u601D\u8003\uFF1A\u5173"
          }
        ),
        thinkOn ? /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("div", { className: "sc-tiers", children: TIERS.map((label, index) => /* @__PURE__ */ (0, import_jsx_runtime38.jsx)(
          "button",
          {
            type: "button",
            className: "sc-tier",
            "aria-pressed": tier === index,
            onClick: () => setTier(index),
            children: label
          },
          label
        )) }) : null
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)("div", { className: isWeChat ? "sc-inputrow" : "sc-inputrow sc-inputrow--qq", children: [
        /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("button", { type: "button", className: "sc-ibtn", children: "\u8BED\u97F3" }),
        isWeChat ? /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("button", { type: "button", className: "sc-ibtn", onClick: () => setGiftOpen(true), children: "\u793C\u7269" }) : null,
        /* @__PURE__ */ (0, import_jsx_runtime38.jsx)(
          "textarea",
          {
            className: "sc-editor",
            rows: 1,
            value: input,
            placeholder: "\u8F93\u5165\u2026",
            "aria-label": "\u8F93\u5165\u2026",
            onChange: (event) => setInput(event.target.value)
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("button", { type: "button", className: "sc-ibtn", onClick: () => setStickersOpen((value) => !value), children: "\u8868\u60C5" }),
        /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("button", { type: "button", className: "sc-ibtn", children: "\u6587\u4EF6" }),
        /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("button", { type: "button", className: "sc-send", children: "\u53D1\u9001" })
      ] })
    ] })
  ] });
  const columns = showListColumn ? isWeChat ? "64px 300px minmax(0, 1fr)" : "300px minmax(0, 1fr)" : "minmax(0, 1fr)";
  return /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)("div", { className: "sc-shell", children: [
    /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)("div", { className: "sc-chat", style: { gridTemplateColumns: columns }, children: [
      isWeChat && showListColumn ? /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)("div", { className: "sc-rail", children: [
        /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { className: "sc-rail__logo", children: "\u521D" }),
        /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("button", { type: "button", className: "sc-rail__item", children: /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { style: { fontSize: "var(--w-size-body)" }, children: railLabel }) }),
        /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("button", { type: "button", className: "sc-rail__item", children: /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { children: "\u597D\u53CB" }) }),
        /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("button", { type: "button", className: "sc-rail__item", children: /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { children: "\u6536\u85CF" }) }),
        /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("button", { type: "button", className: "sc-rail__item", children: /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { children: "\u8868\u60C5" }) }),
        /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("button", { type: "button", className: "sc-rail__item", children: /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { children: "\u8BBE\u7F6E" }) })
      ] }) : null,
      showListColumn ? /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)("div", { className: "sc-listwrap", style: { gridColumn: isWeChat ? 2 : 1, gridRow: "1 / span 2" }, children: [
        isWeChat ? null : /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)("div", { className: "sc-listwrap__head", children: [
          /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("button", { type: "button", className: "sc-iconbtn", "aria-label": "\u8FD4\u56DE", onClick: onBack, children: "<" }),
          /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { className: "sc-forum__title", children: "QQ" }),
          /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { className: "sc-pill", children: "\u89C6\u9891\u53F7" })
        ] }),
        listBody
      ] }) : null,
      header,
      body
    ] }),
    listOpen ? /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)(import_jsx_runtime38.Fragment, { children: [
      /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("button", { type: "button", className: "sc-scrim", "aria-label": "\u5173\u95ED\u5217\u8868", onClick: () => setListOpen(false) }),
      /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)("div", { className: "sc-drawer", children: [
        isWeChat ? null : /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)("div", { className: "sc-listwrap__head", children: [
          /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("button", { type: "button", className: "sc-iconbtn", "aria-label": "\u8FD4\u56DE", onClick: () => setListOpen(false), children: "<" }),
          /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { className: "sc-forum__title", children: "QQ" }),
          /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { className: "sc-pill", children: "\u89C6\u9891\u53F7" })
        ] }),
        listBody
      ] })
    ] }) : null,
    isWeChat && giftOpen ? /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("div", { className: "sc-gift", children: /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)("div", { className: "sc-gift__card", children: [
      /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { className: "sc-gift__title", children: "\u793C\u7269\u7BB1 " }),
      /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { className: "sc-gift__sub", children: "\u70B9\u4E00\u4E0B\u9001\u51FA\u5FC3\u610F\uFF0C\u5BF9\u65B9\u4F1A\u6536\u4E0B\u5E76\u56DE\u5E94" }),
      /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)("div", { className: "sc-gift__modes", children: [
        /* @__PURE__ */ (0, import_jsx_runtime38.jsxs)(
          "button",
          {
            type: "button",
            className: "sc-gift__mode",
            "aria-pressed": giftMode === "gift",
            onClick: () => setGiftMode("gift"),
            children: [
              " ",
              "\u9001\u793C"
            ]
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime38.jsx)(
          "button",
          {
            type: "button",
            className: "sc-gift__mode",
            "aria-pressed": giftMode === "use",
            onClick: () => setGiftMode("use"),
            children: "\u4F7F\u7528"
          }
        )
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("div", { className: "sc-gift__empty", children: /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("span", { children: "\u8FD8\u6CA1\u6709\u5DF2\u8D2D\u5546\u54C1\uFF0C\u5148\u53BB\u5546\u5E97\u901B\u901B\u5427\uFF5E" }) }),
      /* @__PURE__ */ (0, import_jsx_runtime38.jsx)("button", { type: "button", className: "sc-gift__close", onClick: () => setGiftOpen(false), children: "\u5173\u95ED" })
    ] }) }) : null
  ] });
}

// src/screens/save-chat/chat.tsx
var import_jsx_runtime39 = __toESM(require_jsx_runtime(), 1);
var CONTACTS = [
  { id: "shenyan", name: "\u6C88\u781A", last: "\u665A\u5B89~ \u505A\u4E2A\u597D\u68A6\uFF0C\u660E\u5929\u89C1\u3002", time: "22:41", unread: true },
  { id: "xiaoyu", name: "\u5C0F\u96E8", last: "\u6211\u662F\u5C0F\u96E8\u5440\uFF0C\u662F\u4F60\u7684\u966A\u4F34\u8005~ \u4F60\u53EB\u4EC0\u4E48\u540D\u5B57\u5462\uFF1F", time: "\u6628\u5929", unread: false }
];
var MESSAGES = [
  { id: "m1", text: "\u4F60\u597D\u5440\uFF01\u4ECA\u5929\u8FC7\u5F97\u600E\u4E48\u6837\uFF1F\u6709\u4EC0\u4E48\u60F3\u548C\u6211\u804A\u7684\u5417\uFF1F", mine: false },
  { id: "m2", text: "\u665A\u5B89", mine: true },
  { id: "m3", text: "\u665A\u5B89~ \u505A\u4E2A\u597D\u68A6\uFF0C\u660E\u5929\u89C1\u3002", mine: false },
  { id: "m4", text: "\u8C22\u8C22", mine: true },
  { id: "m5", text: "\u4E0D\u5BA2\u6C14\uFF01\u80FD\u5E2E\u5230\u4F60\u662F\u6211\u6700\u5927\u7684\u8363\u5E78~", mine: false }
];
function Chat({ back }) {
  return /* @__PURE__ */ (0, import_jsx_runtime39.jsx)(PageShell, { children: /* @__PURE__ */ (0, import_jsx_runtime39.jsx)(
    ChatShell,
    {
      skin: "wechat",
      contacts: CONTACTS,
      partner: "\u6C88\u781A",
      modeLabel: "\u672C\u5730\u5FAE\u4FE1 \xB7 TA \u8FD8\u5728",
      messages: MESSAGES,
      onBack: back
    }
  ) });
}

// src/screens/save-chat/qqchat.tsx
var import_jsx_runtime40 = __toESM(require_jsx_runtime(), 1);
var CONTACTS2 = [
  { id: "shenyan", name: "\u6C88\u781A", last: "\u665A\u5B89~ \u505A\u4E2A\u597D\u68A6\uFF0C\u660E\u5929\u89C1\u3002", time: "", unread: false },
  { id: "xiaoyu", name: "\u5C0F\u96E8", last: "\u6211\u662F\u5C0F\u96E8\u5440\uFF0C\u662F\u4F60\u7684\u966A\u4F34\u8005~ \u4F60\u53EB\u4EC0\u4E48\u540D\u5B57\u5462\uFF1F", time: "", unread: true }
];
var MESSAGES2 = [
  { id: "q1", text: "\u4F60\u597D\u5440\uFF01\u4ECA\u5929\u8FC7\u5F97\u600E\u4E48\u6837\uFF1F\u6709\u4EC0\u4E48\u60F3\u548C\u6211\u804A\u7684\u5417\uFF1F", mine: false },
  { id: "q2", text: "\u6211\u5728\u7B49\u4F60\u54E6\uFF0C\u4F60\u6765\u5F97\u521A\u597D\u3002", mine: false },
  { id: "q3", text: "\u55EF\u55EF", mine: true },
  { id: "q4", text: "\u6211\u966A\u4F60'\u4E91\u5403\u996D'\uFF0C\u4F60\u60F3\u597D\u8981\u5403\u4EC0\u4E48\u4E86\u5417\uFF1F", mine: false }
];
function Qqchat({ navigate, back }) {
  return /* @__PURE__ */ (0, import_jsx_runtime40.jsx)(PageShell, { children: /* @__PURE__ */ (0, import_jsx_runtime40.jsx)(
    ChatShell,
    {
      skin: "qq",
      contacts: CONTACTS2,
      partner: "\u6C88\u781A",
      modeLabel: "\u672C\u5730 QQ \xB7 TA \u8FD8\u5728",
      messages: MESSAGES2,
      onBack: back,
      onForum: () => navigate("/qqforum")
    }
  ) });
}

// src/screens/save-chat/qqforum.tsx
var import_react30 = __toESM(require_react(), 1);
var import_jsx_runtime41 = __toESM(require_jsx_runtime(), 1);
var TOPICS = [
  { id: "daily", title: "\u65E5\u5E38" },
  { id: "wish", title: "\u613F\u671B" },
  { id: "today", title: "\u4ECA\u5929\u60F3\u505A\u4EC0\u4E48" }
];
var SEED_POSTS = [
  { topic: "daily", author: "\u5C0F\u96E8", text: "\u4ECA\u5929\u5728\u60F3\u7ED9\u4F60\u505A\u7EA2\u8C46\u7CD5\uFF0C\u505A\u597D\u4E86\u53EB\u4F60\u6765\u5C1D\u4E00\u53E3\u3002", time: "\u7F6E\u9876" },
  { topic: "daily", author: "\u5C0F\u6797", text: "\u56FE\u4E66\u9986\u8FD8\u5B8C\u4E66\u4E86\uFF0C\u9760\u7A97\u7684\u4F4D\u7F6E\u6652\u5230\u592A\u9633\uFF0C\u4E00\u4F1A\u53EF\u4EE5\u8FC7\u6765\u3002", time: "\u7F6E\u9876" },
  { topic: "wish", author: "\u5C0F\u96E8", text: "\u613F\u671B\uFF1A\u4E0B\u6B21\u4E00\u8D77\u53BB\u6C5F\u8FB9\u770B\u65E5\u843D\uFF0C\u4F60\u628A\u76F8\u673A\u5E26\u4E0A\u3002", time: "\u7F6E\u9876" },
  { topic: "today", author: "\u5C0F\u6797", text: "\u4ECA\u5929\u60F3\u505A\u70B9\u4EC0\u4E48\uFF1F\u6211\u90FD\u53EF\u4EE5\uFF0C\u6CE1\u8336\u4E5F\u884C\uFF0C\u966A\u4F60\u8BF4\u8BF4\u8BDD\u4E5F\u884C\u3002", time: "\u7F6E\u9876" }
];
function Qqforum({ back }) {
  const [topic, setTopic] = (0, import_react30.useState)("daily");
  const [input, setInput] = (0, import_react30.useState)("");
  const posts = SEED_POSTS.filter((post) => post.topic === topic);
  return /* @__PURE__ */ (0, import_jsx_runtime41.jsx)(PageShell, { children: /* @__PURE__ */ (0, import_jsx_runtime41.jsxs)("div", { className: "sc-forum", children: [
    /* @__PURE__ */ (0, import_jsx_runtime41.jsxs)("div", { className: "sc-forum__bar", children: [
      /* @__PURE__ */ (0, import_jsx_runtime41.jsx)("button", { type: "button", className: "sc-iconbtn", "aria-label": "\u8FD4\u56DE", onClick: back, children: "<" }),
      /* @__PURE__ */ (0, import_jsx_runtime41.jsx)("span", { className: "sc-forum__title", children: "QQ \u8BBA\u575B" }),
      /* @__PURE__ */ (0, import_jsx_runtime41.jsx)("span", {})
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime41.jsx)("div", { className: "sc-topics", children: TOPICS.map((item) => /* @__PURE__ */ (0, import_jsx_runtime41.jsx)(
      "button",
      {
        type: "button",
        className: "sc-topic",
        "aria-pressed": topic === item.id,
        onClick: () => setTopic(item.id),
        children: item.title
      },
      item.id
    )) }),
    /* @__PURE__ */ (0, import_jsx_runtime41.jsx)("div", { className: "sc-posts", children: posts.length === 0 ? /* @__PURE__ */ (0, import_jsx_runtime41.jsx)("p", { className: "cap", children: "\u8FD8\u6CA1\u6709\u5E16\u5B50\uFF0C\u6765\u5F00\u4E2A\u5934\u5427\uFF5E" }) : posts.map((post) => /* @__PURE__ */ (0, import_jsx_runtime41.jsxs)("article", { className: "sc-post", children: [
      /* @__PURE__ */ (0, import_jsx_runtime41.jsxs)("div", { className: "sc-post__head", children: [
        /* @__PURE__ */ (0, import_jsx_runtime41.jsx)("span", { className: post.author === "\u6211" ? "sc-post__author sc-post__author--me" : "sc-post__author", children: post.author }),
        /* @__PURE__ */ (0, import_jsx_runtime41.jsx)("span", { className: "sc-post__time", children: post.time })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime41.jsx)("p", { className: "sc-post__text", children: post.text })
    ] }, post.text)) }),
    /* @__PURE__ */ (0, import_jsx_runtime41.jsxs)("div", { className: "sc-forum__input", children: [
      /* @__PURE__ */ (0, import_jsx_runtime41.jsx)(
        "textarea",
        {
          className: "sc-editor",
          rows: 1,
          value: input,
          placeholder: "\u5728\u8BBA\u575B\u4E0A\u8BF4\u70B9\u4EC0\u4E48\u2026",
          "aria-label": "\u5728\u8BBA\u575B\u4E0A\u8BF4\u70B9\u4EC0\u4E48\u2026",
          onChange: (event) => setInput(event.target.value)
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime41.jsx)("button", { type: "button", className: "sc-send", children: "\u53D1\u5E16" })
    ] })
  ] }) });
}

// src/screens/save-chat/save.tsx
var import_react31 = __toESM(require_react(), 1);
var import_jsx_runtime42 = __toESM(require_jsx_runtime(), 1);
var SLOTS3 = [
  {
    id: "shenyan",
    label: "\u6C88\u781A",
    character: "\u6C88\u781A",
    scene: "\u7B2C\u4E09\u7AE0 \xB7 \u706F\u4E0B\u8865\u4E66",
    savedAt: "11/26 22:41",
    historyNote: "\u4ECE 128 \u6761\u804A\u5929\u8BB0\u5F55\u8D77\u6B65 \xB7 3 \u5929"
  },
  {
    id: "xiaoyu",
    label: "\u5C0F\u96E8",
    character: "\u5C0F\u96E8",
    scene: "\u7B2C\u4E09\u7AE0 \xB7 \u706F\u4E0B\u8865\u4E66",
    savedAt: "11/24 21:07",
    historyNote: ""
  }
];
function Save({ navigate, back }) {
  const [slots] = (0, import_react31.useState)(SLOTS3);
  return /* @__PURE__ */ (0, import_jsx_runtime42.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime42.jsx)(
      PageHeader,
      {
        title: "\u5B58\u6863\u7BA1\u7406",
        onBack: back,
        right: /* @__PURE__ */ (0, import_jsx_runtime42.jsxs)(import_jsx_runtime42.Fragment, { children: [
          /* @__PURE__ */ (0, import_jsx_runtime42.jsx)(TextBtn, { onClick: () => navigate("/superMonitor"), children: "\u76D1\u6D4B" }),
          /* @__PURE__ */ (0, import_jsx_runtime42.jsx)(TextBtn, { onClick: () => navigate("/saveTree"), children: "\u5206\u652F\u6811" })
        ] })
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime42.jsxs)("div", { className: "screen", children: [
      /* @__PURE__ */ (0, import_jsx_runtime42.jsx)(Card, { tight: true, children: /* @__PURE__ */ (0, import_jsx_runtime42.jsxs)("button", { type: "button", className: "sc-cloud", onClick: () => navigate("/cloudsaves"), children: [
        /* @__PURE__ */ (0, import_jsx_runtime42.jsx)("span", { className: "sc-well", children: "\u4E91" }),
        /* @__PURE__ */ (0, import_jsx_runtime42.jsxs)("span", { className: "sc-slot__text", children: [
          /* @__PURE__ */ (0, import_jsx_runtime42.jsx)("span", { className: "sc-slot__title", children: "\u4E91\u5B58\u6863" }),
          /* @__PURE__ */ (0, import_jsx_runtime42.jsx)("span", { className: "sc-slot__desc", children: "\u53D6\u56DE\u5176\u4ED6\u8BBE\u5907\u7684\u5B58\u6863\uFF0C\u6216\u6062\u590D\u540C\u6B65\u8986\u76D6\u524D\u7684\u5386\u53F2\u7248\u672C" })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime42.jsx)("span", { style: { color: "var(--w-ink-muted)", fontSize: "var(--w-size-body)" }, children: "\u203A" })
      ] }) }),
      /* @__PURE__ */ (0, import_jsx_runtime42.jsx)(Card, { tight: true, children: slots.length === 0 ? /* @__PURE__ */ (0, import_jsx_runtime42.jsxs)("div", { className: "sc-empty", children: [
        /* @__PURE__ */ (0, import_jsx_runtime42.jsx)("span", { children: "\u8FD8\u6CA1\u6709\u5B58\u6863" }),
        /* @__PURE__ */ (0, import_jsx_runtime42.jsx)("small", { children: "\u8FD9\u53F0\u673A\u5668\u4E0A\u8FD8\u6CA1\u6709\u4EFB\u4F55\u4E00\u5C40" }),
        /* @__PURE__ */ (0, import_jsx_runtime42.jsx)("small", { style: { color: "var(--w-signal)" }, children: "\u70B9\u4E0B\u65B9\u300C\u65B0\u5EFA\u5B58\u6863\u300D\u5F00\u59CB\u65B0\u7684\u4E00\u5C40" })
      ] }) : slots.map((slot, index) => /* @__PURE__ */ (0, import_jsx_runtime42.jsxs)("div", { children: [
        /* @__PURE__ */ (0, import_jsx_runtime42.jsxs)("div", { className: "sc-slot", children: [
          /* @__PURE__ */ (0, import_jsx_runtime42.jsx)("span", { className: "sc-slot__avatar", children: slot.character.slice(0, 1) }),
          /* @__PURE__ */ (0, import_jsx_runtime42.jsxs)("div", { className: "sc-slot__text", children: [
            /* @__PURE__ */ (0, import_jsx_runtime42.jsx)("span", { className: "sc-slot__title", children: slot.label }),
            /* @__PURE__ */ (0, import_jsx_runtime42.jsxs)("span", { className: "sc-slot__desc", children: [
              /* @__PURE__ */ (0, import_jsx_runtime42.jsx)("b", { style: { fontWeight: 600, color: "var(--w-ink-body)" }, children: slot.character }),
              " \xB7 ",
              slot.scene,
              " \xB7 ",
              slot.savedAt
            ] }),
            slot.historyNote ? /* @__PURE__ */ (0, import_jsx_runtime42.jsx)("span", { className: "sc-slot__note", children: slot.historyNote }) : null,
            /* @__PURE__ */ (0, import_jsx_runtime42.jsxs)("div", { className: "sc-slot__acts", children: [
              /* @__PURE__ */ (0, import_jsx_runtime42.jsx)(TextBtn, { children: "\u91CD\u547D\u540D" }),
              /* @__PURE__ */ (0, import_jsx_runtime42.jsx)(TextBtn, { children: "\u5206\u652F" }),
              /* @__PURE__ */ (0, import_jsx_runtime42.jsx)(TextBtn, { children: "\u5BFC\u51FA" }),
              /* @__PURE__ */ (0, import_jsx_runtime42.jsx)(TextBtn, { children: "\u6743\u9650" }),
              /* @__PURE__ */ (0, import_jsx_runtime42.jsx)(TextBtn, { children: "\u5220\u9664" })
            ] })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime42.jsx)(GlassButton, { size: "sm", className: "sc-load", onClick: () => navigate("/main"), children: "\u8BFB\u53D6" })
        ] }),
        index < slots.length - 1 ? /* @__PURE__ */ (0, import_jsx_runtime42.jsx)(Sep, {}) : null
      ] }, slot.id)) }),
      /* @__PURE__ */ (0, import_jsx_runtime42.jsxs)("div", { className: "sc-bottom", children: [
        /* @__PURE__ */ (0, import_jsx_runtime42.jsx)(GlassButton, { size: "lg", className: "sc-wide", onClick: () => navigate("/select"), children: "\u65B0\u5EFA\u5B58\u6863" }),
        /* @__PURE__ */ (0, import_jsx_runtime42.jsxs)("div", { className: "sc-bottom__pair", children: [
          /* @__PURE__ */ (0, import_jsx_runtime42.jsx)(GlassButton, { size: "md", children: "\u5BFC\u5165\u5B58\u6863" }),
          /* @__PURE__ */ (0, import_jsx_runtime42.jsx)(GlassButton, { size: "md", children: "\u5907\u4EFD\u5168\u90E8" })
        ] })
      ] })
    ] })
  ] });
}

// src/screens/save-chat/saveTree.tsx
var import_react32 = __toESM(require_react(), 1);
var import_jsx_runtime43 = __toESM(require_jsx_runtime(), 1);
var NODES2 = [
  {
    id: "n1",
    label: "\u6C88\u781A",
    parentId: "",
    column: 0,
    lane: 0,
    date: "11/24",
    savedAtText: "11-24 21:07",
    scene: "\u7B2C\u4E09\u7AE0 \xB7 \u706F\u4E0B\u8865\u4E66",
    world: "\u9ED8\u8BA4\u4E16\u754C",
    duration: "2 \u5C0F\u65F6 37 \u5206",
    branch: ""
  },
  {
    id: "n2",
    label: "\u6C88\u781A \xB7 \u5206\u652F",
    parentId: "n1",
    column: 1,
    lane: 1,
    date: "11/25",
    savedAtText: "11-25 20:15",
    scene: "\u7B2C\u4E09\u7AE0 \xB7 \u706F\u4E0B\u8865\u4E66",
    world: "\u9ED8\u8BA4\u4E16\u754C",
    duration: "3 \u5C0F\u65F6 02 \u5206",
    branch: "\u6C88\u781A \xB7 \u5206\u652F"
  },
  {
    id: "n4",
    label: "\u6C88\u781A \xB7 \u5206\u652F",
    parentId: "n2",
    column: 2,
    lane: 2,
    date: "11/25",
    savedAtText: "11-25 23:02",
    scene: "\u7B2C\u4E09\u7AE0 \xB7 \u706F\u4E0B\u8865\u4E66",
    world: "\u9ED8\u8BA4\u4E16\u754C",
    duration: "3 \u5C0F\u65F6 11 \u5206",
    branch: "\u6C88\u781A \xB7 \u5206\u652F"
  },
  {
    id: "n3",
    label: "\u6C88\u781A \xB7 \u5206\u652F",
    parentId: "n1",
    column: 3,
    lane: 0,
    date: "11/26",
    savedAtText: "11-26 22:41",
    scene: "\u7B2C\u4E09\u7AE0 \xB7 \u706F\u4E0B\u8865\u4E66",
    world: "\u9ED8\u8BA4\u4E16\u754C",
    duration: "2 \u5C0F\u65F6 27 \u5206",
    branch: "\u6C88\u781A \xB7 \u5206\u652F",
    current: true
  }
];
var CARD_W = 92;
var CARD_H = 40;
var LANE_H = 54;
var COLUMN_W = 104;
var LEFT_PAD = 12;
var RIGHT_PAD = 12;
var TOP_PAD = 8;
var AXIS_H = 30;
var PANEL_W = 216;
var PANEL_GAP = 10;
function nodeX(node) {
  return LEFT_PAD + node.column * COLUMN_W;
}
function nodeY(node) {
  return TOP_PAD + AXIS_H + node.lane * LANE_H;
}
function SaveTree({ navigate, back }) {
  const [rootId, setRootId] = (0, import_react32.useState)("n1");
  const [selectedId, setSelectedId] = (0, import_react32.useState)("");
  const lanes = Math.max(...NODES2.map((node) => node.lane)) + 1;
  const width = LEFT_PAD + NODES2.length * COLUMN_W + RIGHT_PAD;
  const height = TOP_PAD + AXIS_H + lanes * LANE_H + 8;
  const selected = NODES2.find((node) => node.id === selectedId) ?? null;
  return /* @__PURE__ */ (0, import_jsx_runtime43.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime43.jsx)(
      PageHeader,
      {
        title: "\u5206\u652F\u6811",
        onBack: back,
        right: /* @__PURE__ */ (0, import_jsx_runtime43.jsxs)(import_jsx_runtime43.Fragment, { children: [
          /* @__PURE__ */ (0, import_jsx_runtime43.jsx)(
            Select,
            {
              label: "\u9009\u62E9\u8D77\u59CB\u5B58\u6863",
              value: rootId,
              options: [{ value: "n1", label: "\u6C88\u781A \xB7 11-24 21:07" }],
              onChange: setRootId
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime43.jsx)("span", { style: selectedId ? void 0 : { opacity: 0.5, pointerEvents: "none" }, "aria-disabled": !selectedId, children: /* @__PURE__ */ (0, import_jsx_runtime43.jsx)(GlassButton, { size: "sm", className: "sc-load", onClick: () => navigate("/save"), children: "\u53BB\u8FD9\u4E2A\u5B58\u6863" }) })
        ] })
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime43.jsx)("div", { className: "screen", children: /* @__PURE__ */ (0, import_jsx_runtime43.jsxs)(Card, { children: [
      /* @__PURE__ */ (0, import_jsx_runtime43.jsxs)("div", { className: "sc-tree", children: [
        /* @__PURE__ */ (0, import_jsx_runtime43.jsxs)("svg", { width, height, role: "img", "aria-label": "\u5B58\u6863\u5206\u652F\u6811", children: [
          NODES2.filter((node) => node.parentId).map((node) => {
            const parent = NODES2.find((candidate) => candidate.id === node.parentId);
            if (!parent) return null;
            const x1 = nodeX(parent) + CARD_W;
            const y1 = nodeY(parent) + CARD_H / 2;
            const x2 = nodeX(node);
            const y2 = nodeY(node) + CARD_H / 2;
            const mid = x1 + (x2 - x1) / 2;
            return /* @__PURE__ */ (0, import_jsx_runtime43.jsx)(
              "path",
              {
                d: `M ${x1} ${y1} H ${mid} V ${y2} H ${x2}`,
                fill: "none",
                stroke: "var(--w-hairline)",
                strokeWidth: 1.5
              },
              `${parent.id}-${node.id}`
            );
          }),
          NODES2.map((node) => {
            const active = node.id === selectedId;
            const current = Boolean(node.current);
            return /* @__PURE__ */ (0, import_jsx_runtime43.jsxs)(
              "g",
              {
                className: "sc-tree__node",
                onClick: () => setSelectedId(active ? "" : node.id),
                role: "button",
                "aria-label": node.label,
                children: [
                  /* @__PURE__ */ (0, import_jsx_runtime43.jsx)(
                    "text",
                    {
                      x: nodeX(node) + CARD_W / 2,
                      y: TOP_PAD + 12,
                      textAnchor: "middle",
                      fontSize: 11,
                      fill: "var(--w-ink-faint)",
                      children: node.date
                    }
                  ),
                  /* @__PURE__ */ (0, import_jsx_runtime43.jsx)(
                    "rect",
                    {
                      x: nodeX(node),
                      y: nodeY(node),
                      width: CARD_W,
                      height: CARD_H,
                      rx: 8,
                      fill: "var(--w-tint-card)",
                      stroke: active || current ? "var(--w-signal)" : "var(--w-hairline)",
                      strokeWidth: active || current ? 1.5 : 1
                    }
                  ),
                  /* @__PURE__ */ (0, import_jsx_runtime43.jsx)(
                    "text",
                    {
                      x: nodeX(node) + CARD_W / 2,
                      y: nodeY(node) + CARD_H / 2 + 4,
                      textAnchor: "middle",
                      fontSize: 11,
                      fill: "var(--w-ink-body)",
                      children: node.label.length > 7 ? `${node.label.slice(0, 7)}\u2026` : node.label
                    }
                  ),
                  current ? /* @__PURE__ */ (0, import_jsx_runtime43.jsx)(
                    "circle",
                    {
                      cx: nodeX(node) + CARD_W - 8,
                      cy: nodeY(node) + 8,
                      r: 3,
                      fill: "var(--w-signal)"
                    }
                  ) : null
                ]
              },
              node.id
            );
          })
        ] }),
        selected ? /* @__PURE__ */ (0, import_jsx_runtime43.jsxs)(
          "div",
          {
            className: "sc-panel",
            style: {
              left: Math.min(nodeX(selected) + CARD_W + PANEL_GAP, width - PANEL_W),
              top: Math.max(nodeY(selected) - 6, 0)
            },
            children: [
              /* @__PURE__ */ (0, import_jsx_runtime43.jsx)("span", { className: "sc-panel__title", children: selected.label }),
              [
                ["\u89D2\u8272", "\u6C88\u781A"],
                ["\u573A\u666F", selected.scene],
                ["\u4E16\u754C", selected.world],
                ["\u65F6\u957F", selected.duration],
                ["\u4FDD\u5B58", selected.savedAtText],
                ["\u7236\u6863", selected.parentId ? "\u6C88\u781A" : "\u65E0\uFF08\u8D77\u59CB\u5B58\u6863\uFF09"],
                ["\u5206\u652F", selected.branch || "\u65E0\u5206\u652F"]
              ].map(([key, value]) => /* @__PURE__ */ (0, import_jsx_runtime43.jsxs)("span", { className: "sc-panel__row", children: [
                key,
                /* @__PURE__ */ (0, import_jsx_runtime43.jsx)("b", { children: value })
              ] }, key))
            ]
          }
        ) : null
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime43.jsxs)("p", { className: "cap", children: [
        "\u5171 ",
        NODES2.length,
        " \u4E2A\u5B58\u6863 \xB7 1 \u6761\u8D77\u59CB\u5B58\u6863 \xB7 \u5F53\u524D\u6811 ",
        NODES2.length,
        " \u4E2A"
      ] })
    ] }) })
  ] });
}

// src/screens/save-chat/index.tsx
var screens5 = {
  save: Save,
  saveTree: SaveTree,
  chat: Chat,
  qqchat: Qqchat,
  qqforum: Qqforum,
  bindwizard: Bindwizard
};

// src/screens/section/settingssection.tsx
var import_react34 = __toESM(require_react(), 1);

// src/components/SignalButton.tsx
var import_jsx_runtime44 = __toESM(require_jsx_runtime(), 1);
function SignalButton({ children, onClick, disabled, busy, className = "" }) {
  const press = usePress();
  return /* @__PURE__ */ (0, import_jsx_runtime44.jsx)(
    Glass,
    {
      as: "button",
      type: "button",
      tone: "signal",
      radius: 8,
      className: `signal ${className}`.trim(),
      elementRef: press.ref,
      disabled,
      "aria-busy": busy || void 0,
      onPointerDown: press.down,
      onPointerUp: press.up,
      onPointerLeave: press.up,
      onPointerCancel: press.up,
      onClick,
      children: /* @__PURE__ */ (0, import_jsx_runtime44.jsx)("span", { className: "signal__label", children })
    }
  );
}

// src/components/SettingCard.tsx
var import_jsx_runtime45 = __toESM(require_jsx_runtime(), 1);
function SettingCard({
  title: title2,
  note,
  children,
  className = ""
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime45.jsxs)(Glass, { as: "section", tone: "card", radius: 12, className: `s-card ${className}`.trim(), children: [
    /* @__PURE__ */ (0, import_jsx_runtime45.jsx)("h2", { className: "s-card__title", children: title2 }),
    note ? /* @__PURE__ */ (0, import_jsx_runtime45.jsx)("p", { className: "s-card__note", children: note }) : null,
    /* @__PURE__ */ (0, import_jsx_runtime45.jsx)("div", { className: "s-rows", children })
  ] });
}
function SettingRow({
  title: title2,
  desc: desc2,
  control,
  gap = false,
  className = ""
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime45.jsxs)("div", { className: `s-row${gap ? " s-row--gap" : ""} ${className}`.trim(), children: [
    /* @__PURE__ */ (0, import_jsx_runtime45.jsxs)("div", { className: "s-row__text", children: [
      /* @__PURE__ */ (0, import_jsx_runtime45.jsx)("span", { className: "s-row__title", children: title2 }),
      desc2 ? /* @__PURE__ */ (0, import_jsx_runtime45.jsx)("span", { className: "s-row__desc", children: desc2 }) : null
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime45.jsx)("div", { className: "s-row__control", children: control })
  ] });
}
function Sep2() {
  return /* @__PURE__ */ (0, import_jsx_runtime45.jsx)("div", { className: "s-sep", role: "presentation" });
}
function SettingBlock({ children, className = "" }) {
  return /* @__PURE__ */ (0, import_jsx_runtime45.jsx)("div", { className: `s-block ${className}`.trim(), children });
}

// src/lib/appearance.ts
var SOLID_SWATCHES = [
  { hex: "#FDF8F0", name: "\u5976\u6CB9\u7EB8" },
  { hex: "#E8E6E1", name: "\u7D20\u7070" },
  { hex: "#DDE6F0", name: "\u6668\u96FE\u84DD" },
  { hex: "#CBDCD8", name: "\u9752\u74F7" },
  { hex: "#F3E3CF", name: "\u6696\u7802" },
  { hex: "#F6E1E6", name: "\u6A31\u96FE" },
  { hex: "#3A3D42", name: "\u77F3\u58A8" },
  { hex: "#2C3A4E", name: "\u5E7D\u84DD" },
  { hex: "#4A3527", name: "\u8D6D\u77F3" },
  { hex: "#22322A", name: "\u58A8\u7EFF" }
];
var BACKGROUND_MODE_CHOICES = [
  { mode: "solid", label: "\u7EAF\u8272\uFF08\u9ED8\u8BA4\uFF09" },
  { mode: "image", label: "\u81EA\u5B9A\u4E49\u56FE\u7247" },
  { mode: "clear", label: "\u7A97\u53E3\u900F\u89C6\uFF08\u770B\u7A7F\u5230\u684C\u9762\uFF09" }
];
function solidHint(hex) {
  if (!hex) return "\u80CC\u666F\u8DDF\u7740\u4E3B\u9898\u8D70";
  const named = SOLID_SWATCHES.find((swatch) => swatch.hex.toLowerCase() === hex.toLowerCase());
  return named ? `\u80CC\u666F\uFF1A${named.name} ${named.hex}` : `\u80CC\u666F\uFF1A${hex}`;
}
function pageTintHint(hex) {
  return hex ? `\u9875\u9762\uFF1A${hex}` : "\u9875\u9762\u8DDF\u7740\u8272\u677F\u8D70";
}
var solidVisible = (mode) => mode !== "image" && mode !== "clear";

// src/lib/useAppearance.ts
var import_react33 = __toESM(require_react(), 1);

// src/lib/defaults.ts
var DEFAULT_APPEARANCE = {
  alpha: 0.85,
  frost: 0.5,
  liquid: 0.4,
  lightweight: false,
  reducedTransparency: false,
  background: { mode: "solid", solidHex: "", pageTintHex: "", imageLabel: "\u5C1A\u672A\u9009\u62E9\u56FE\u7247" },
  scene: { experimental: false, weather: true, light: true, audio: true, audioLevel: 0.6 }
};
var DEFAULT_SETTINGS = {
  appearance: { liquid: true, glass: true, frost: true, theme: "endfield" },
  sound: { bgm: 0.7, sfx: 0.8 },
  text: { scale: 1 },
  story: { autoSave: true },
  language: { locale: "zh-CN" },
  ai: { providers: 2, smartSaving: false, timeoutSeconds: 60 },
  memory: { windowDays: 7, contextLimit: "32K" },
  ui: { orientation: "auto", chatStyle: "galgame", difficulty: "normal" },
  platform: { petIdleMinutes: 10, preferDiscreteGpu: false },
  integrations: { qq: false, wechat: false, mcp: true },
  life: { notifications: true, weatherCity: "\u676D\u5DDE", customWeatherSource: false },
  data: { skillsInstalled: false }
};

// src/bridge/mock.ts
var SAMPLE_SAVE = {
  character: "\u6C88\u781A",
  scene: "\u7B2C\u4E09\u7AE0 \xB7 \u706F\u4E0B\u8865\u4E66",
  savedAt: "2026-11-26T22:41:00+08:00",
  minutes: 147
};
var store = {
  appearance: structuredClone(DEFAULT_APPEARANCE),
  values: structuredClone(DEFAULT_SETTINGS),
  save: SAMPLE_SAVE
};

// src/bridge/client.ts
var BridgeFailure = class extends Error {
  code;
  detail;
  constructor(code, message, detail) {
    super(message);
    this.name = "BridgeFailure";
    this.code = code;
    this.detail = detail;
  }
};
function toErrorBody(error) {
  if (error instanceof BridgeFailure) {
    return { code: error.code, message: error.message, detail: error.detail };
  }
  if (error instanceof Error) return { code: "E_BACKEND", message: error.message };
  return { code: "E_BACKEND", message: "\u672A\u77E5\u9519\u8BEF\u3002" };
}

// src/lib/useAppearance.ts
var SAVE_DEBOUNCE_MS = 300;
var SYNC_EVENT = "wab:appearance";
function announce(next) {
  window.dispatchEvent(new CustomEvent(SYNC_EVENT, { detail: next }));
}
function useAppearance(bridge2) {
  const [value, setValue] = (0, import_react33.useState)(DEFAULT_APPEARANCE);
  const [status, setStatus] = (0, import_react33.useState)("loading");
  const [error, setError] = (0, import_react33.useState)(null);
  const [save, setSave] = (0, import_react33.useState)("idle");
  const latest = (0, import_react33.useRef)(DEFAULT_APPEARANCE);
  const timer = (0, import_react33.useRef)(null);
  (0, import_react33.useEffect)(
    () => () => {
      if (timer.current) window.clearTimeout(timer.current);
    },
    []
  );
  const reload = (0, import_react33.useCallback)(() => {
    setStatus("loading");
    setError(null);
    bridge2.request("appearance.get", {}).then(
      ({ appearance }) => {
        if (!appearance) {
          setStatus("empty");
          return;
        }
        latest.current = appearance;
        setValue(appearance);
        setStatus("ready");
      },
      (cause) => {
        setError(toErrorBody(cause));
        setStatus("error");
      }
    );
  }, [bridge2]);
  (0, import_react33.useEffect)(() => {
    reload();
  }, [reload]);
  const persist = (0, import_react33.useCallback)(
    (next) => {
      setSave("saving");
      bridge2.request("appearance.set", { appearance: next }).then(
        ({ appearance }) => {
          latest.current = appearance;
          setValue(appearance);
          setStatus("ready");
          setSave("idle");
        },
        () => setSave("error")
      );
    },
    [bridge2]
  );
  const update = (0, import_react33.useCallback)(
    (next) => {
      latest.current = next;
      setValue(next);
      announce(next);
      setSave("saving");
      if (timer.current) window.clearTimeout(timer.current);
      timer.current = window.setTimeout(() => persist(latest.current), SAVE_DEBOUNCE_MS);
    },
    [persist]
  );
  const replace = (0, import_react33.useCallback)((next) => {
    latest.current = next;
    setValue(next);
    setStatus("ready");
  }, []);
  (0, import_react33.useEffect)(() => {
    const onSync = (event) => {
      const next = event.detail;
      if (!next || next === latest.current) return;
      latest.current = next;
      setValue(next);
    };
    window.addEventListener(SYNC_EVENT, onSync);
    return () => window.removeEventListener(SYNC_EVENT, onSync);
  }, []);
  const retrySave = (0, import_react33.useCallback)(() => persist(latest.current), [persist]);
  return { value, status, error, save, reload, update, replace, retrySave };
}

// src/screens/section/settingssection.tsx
var import_jsx_runtime46 = __toESM(require_jsx_runtime(), 1);
var SECTIONS = [
  { key: "appearance", title: "\u5916\u89C2\u4E0E\u4E3B\u9898", desc: "\u73BB\u7483\u6548\u679C\u3001\u5149\u6E90\u65B9\u5411\u3001\u9875\u9762\u80CC\u666F\u4E0E\u5929\u6C14\u53E0\u52A0" },
  {
    key: "sound",
    title: "\u58F0\u97F3\u4E0E\u8BED\u97F3",
    desc: "\u80CC\u666F\u97F3\u4E50\u3001\u97F3\u6548\u3001\u6309\u952E/\u6253\u5B57\u97F3\uFF1B\u6717\u8BFB\u4E0E\u8BED\u97F3\u8BC6\u522B\u3001\u97F3\u8272\u3001\u5916\u90E8\u8BED\u97F3\u670D\u52A1"
  },
  { key: "ai", title: "AI \u4E0E\u6A21\u578B", desc: "\u5382\u5546\u4E0E\u6A21\u578B\u3001\u5BC6\u94A5\u3001\u8BC6\u56FE\u3001\u91CF\u5316\u3001\u82B1\u8D39\u4E0E\u7701\u94B1\u3001AI \u7ED8\u753B" },
  { key: "memory", title: "\u8BB0\u5FC6\u4E0E\u5BF9\u8BDD", desc: "\u8BB0\u591A\u5C11\u3001\u8BB0\u591A\u4E45\u3001\u4E0A\u4E0B\u6587\u4E0A\u9650\u3001\u6838\u5FC3\u8BB0\u5FC6\u3001\u521B\u4F24\u4E0E\u94FE\u5F0F\u56DE\u5FC6" },
  {
    key: "persona",
    title: "\u8EAB\u4EFD\u4E0E\u5FC3\u7406",
    desc: "\u65C1\u767D AI\u3001\u4F60\u7684\u6027\u522B\u3001\u81EA\u5B9A\u4E49\u6027\u522B\u3001\u5FC3\u7406\u72B6\u51B5\u4E0E\u8EAB\u4F53\u611F\u53D7\u3001\u5927\u8111\u9884\u5224"
  },
  {
    key: "interface",
    title: "\u754C\u9762\u4E0E\u73A9\u6CD5",
    desc: "\u5C4F\u5E55\u65B9\u5411\u3001\u804A\u5929\u98CE\u683C\u3001\u5730\u56FE\u5E95\u56FE\u4E0E\u79BB\u7EBF\u5305\u3001\u5C0F\u6E38\u620F\u96BE\u5EA6\u4E0E\u68CB\u529B\u3001\u91D1\u94B1\u7CFB\u7EDF\u3001\u5C0F\u7EC4\u4EF6\u3001\u7740\u88C5\u4E0E\u6027\u6B32"
  },
  {
    key: "platform",
    title: "\u5E73\u53F0\u4E0E\u8FD0\u884C",
    desc: "\u684C\u5BA0\u3001\u7A97\u53E3\u4E0E\u591A\u5F00\u3001\u663E\u5361\u504F\u597D\u3001\u5916\u90E8\u5DE5\u5177\u8FD0\u884C\u65F6\u3001\u5B9E\u9A8C\u529F\u80FD"
  },
  {
    key: "integrations",
    title: "\u5916\u90E8\u63A5\u5165",
    desc: "QQ \u4E0E\u5FAE\u4FE1\u673A\u5668\u4EBA\u3001MCP \u670D\u52A1\u3001\u5DE5\u5177\u5B89\u5168\u3001\u63D2\u4EF6\u4E0E\u5DE5\u574A\u3001\u89C6\u9891\u53F7\u5916\u94FE"
  },
  {
    key: "life",
    title: "\u901A\u77E5\u4E0E\u751F\u6D3B",
    desc: "\u901A\u77E5\u4E0E\u95EE\u5019\u3001\u6D6E\u52A8\u901A\u77E5\u7684\u6837\u5F0F\u4E0E\u4F4D\u7F6E\u3001\u5929\u6C14\u3001\u751F\u7406\u5468\u671F\u3001\u5B58\u6863\u8FD0\u884C\u65F6"
  },
  { key: "data", title: "\u6570\u636E\u4E0E\u7D20\u6750", desc: "\u6570\u636E\u5BFC\u5165\u5BFC\u51FA\u3001\u4E91\u5B58\u6863\u3001\u81EA\u52A8\u66F4\u65B0\u3001\u7D20\u6750\u5E93\u3001\u80FD\u529B\u5305" },
  { key: "advanced", title: "\u5176\u4ED6\u4E0E\u5F00\u53D1\u8005", desc: "\u652F\u6301\u4F5C\u8005\u3001\u5F00\u53D1\u8005\u5C55\u793A\u6A21\u5F0F" }
];
function useBool(initial) {
  const [value, setValue] = (0, import_react34.useState)(initial);
  return [value, setValue];
}
function useStr(initial) {
  const [value, setValue] = (0, import_react34.useState)(initial);
  return [value, setValue];
}
function useNum(initial) {
  const [value, setValue] = (0, import_react34.useState)(initial);
  return [value, setValue];
}
var opts = (labels) => labels.map((label) => ({ value: label, label }));
function PickRow({
  title: title2,
  desc: desc2,
  value,
  options: options2,
  onChange,
  width = 190,
  disabled,
  label
}) {
  const list = opts(options2);
  return /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
    SettingRow,
    {
      title: title2,
      desc: desc2,
      control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        "span",
        {
          style: {
            display: "inline-flex",
            width,
            opacity: disabled ? 0.5 : void 0,
            pointerEvents: disabled ? "none" : void 0
          },
          children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            Select,
            {
              value: list.some((o) => o.value === value) ? value : list[0]?.value ?? "",
              options: list,
              onChange,
              label: label ?? title2
            }
          )
        }
      )
    }
  );
}
function FieldRow({
  title: title2,
  value,
  onChange,
  placeholder,
  type = "text",
  width = 320
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
    SettingRow,
    {
      title: title2,
      control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { style: { display: "inline-block", width: `min(${width}px, 44vw)` }, children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Field, { value, onChange, placeholder, type, label: title2 }) })
    }
  );
}
function RangeSlider({
  label,
  min,
  max,
  step = 0.01,
  value,
  onChange,
  width = 130,
  disabled
}) {
  const span = max - min || 1;
  return /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
    GlassSlider,
    {
      label,
      width,
      disabled,
      value: Math.min(1, Math.max(0, (value - min) / span)),
      onChange: (t) => {
        const raw = min + t * span;
        onChange(Math.round(raw / step) * step);
      }
    }
  );
}
function Sw({
  label,
  checked,
  onChange,
  disabled
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(GlassSwitch, { label, checked, onChange, disabled });
}
function Btn({
  children,
  onClick,
  disabled
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { style: { opacity: disabled ? 0.5 : void 0, pointerEvents: disabled ? "none" : void 0 }, children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(GlassButton, { size: "md", className: "gbtn--tight", onClick, children }) });
}
function BtnRow({ children }) {
  return /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("div", { className: "btnrow", children });
}
function Divider() {
  return /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("div", { style: { height: 1, margin: "4px 0", background: "var(--w-hairline)" }, role: "presentation" });
}
function SettingsSection({ bridge: bridge2, query, back }) {
  const raw = (query.get("key") ?? "").trim();
  const key = raw === "window" ? "platform" : raw === "voice" ? "sound" : raw;
  const section2 = SECTIONS.find((item) => item.key === key);
  const body = !section2 ? /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Empty, { title: "\u672A\u77E5\u5206\u7C7B", reason: raw }) : section2.key === "appearance" ? /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(AppearanceSection, { bridge: bridge2 }) : section2.key === "sound" ? /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(SoundSection, {}) : section2.key === "ai" ? /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(AiSection, {}) : section2.key === "memory" ? /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(MemorySection, {}) : section2.key === "persona" ? /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(PersonaSection, {}) : section2.key === "interface" ? /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(InterfaceSection, {}) : section2.key === "platform" ? /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(PlatformSection, {}) : section2.key === "integrations" ? /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(IntegrationsSection, {}) : section2.key === "life" ? /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(LifeSection, {}) : section2.key === "data" ? /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(DataSection, {}) : /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(AdvancedSection, {});
  return /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
      PageHeader,
      {
        title: section2 ? section2.title : "\u672A\u77E5\u5206\u7C7B",
        subtitle: section2 ? section2.desc : raw,
        onBack: back
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("main", { className: "section", children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("div", { className: "section__body", children: body }) })
  ] });
}
function AppearanceSection({ bridge: bridge2 }) {
  const store2 = useAppearance(bridge2);
  const { value } = store2;
  const set = (patch) => store2.update({ ...value, ...patch });
  const glassActive = value.alpha > 0.01;
  if (store2.status === "loading") {
    return /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Glass, { as: "section", tone: "card", radius: 12, className: "s-card", "aria-busy": "true", children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "cap", children: "\u6B63\u5728\u8BFB\u53D6\u8BBE\u7F6E\u2026" }) });
  }
  if (store2.status === "error") {
    return /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Card, { title: "\u8BFB\u4E0D\u5230\u8BBE\u7F6E", note: "\u672C\u5730\u670D\u52A1\u8FD9\u6B21\u6CA1\u6709\u56DE\u8BDD\u3002\u786E\u8BA4\u540E\u53F0\u8FD8\u5728\u8FD0\u884C\uFF0C\u518D\u8BD5\u4E00\u6B21\u3002", children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(BtnRow, { children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { onClick: store2.reload, children: "\u91CD\u65B0\u8BFB\u53D6" }) }) });
  }
  if (store2.status === "empty") {
    return /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Card, { title: "\u6CA1\u6709\u53EF\u8C03\u7684\u5916\u89C2", note: "\u540E\u7AEF\u8FD8\u6CA1\u6709\u4E0B\u53D1\u5916\u89C2\u53C2\u6570\u3002\u7B49\u5B83\u6302\u4E0A\u6765\u518D\u770B\u3002", children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(BtnRow, { children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { onClick: store2.reload, children: "\u518D\u8BFB\u4E00\u6B21" }) }) });
  }
  return /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(import_jsx_runtime46.Fragment, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u73BB\u7483\u6548\u679C", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u8F7B\u91CF\u6A21\u5F0F\uFF08\u5173\u9634\u5F71\uFF0C\u4F4E\u914D\u66F4\u6D41\u7545\uFF09",
          desc: "\u5173\u95ED\u540E\u5361\u7247\u4E0D\u518D\u6709\u6295\u5F71\uFF0C\u89C2\u611F\u6734\u7D20\u4E00\u4E9B\u3002\u9634\u5F71\u5728 WinUI \u4E0A\u662F\u6BCF\u4E2A\u5143\u7D20\u4E00\u4E2A\u5408\u6210\u5C42\uFF0C\u5361\u7247\u591A\u6216\u663E\u5361\u5F31\u65F6\u66F4\u5212\u7B97\uFF1B\u672C\u673A\uFF08\u72EC\u7ACB\u663E\u5361\uFF09\u5B9E\u6D4B\u5DEE\u5F02\u5728\u566A\u58F0\u8303\u56F4\u5185\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u8F7B\u91CF\u6A21\u5F0F", checked: value.lightweight, onChange: (lightweight) => set({ lightweight }) })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          gap: true,
          title: "\u73BB\u7483\u900F\u660E\u5EA6",
          desc: "0 = \u8868\u9762\u4E0D\u900F\u660E\uFF0C\u7B49\u540C\u4E8E\u5173\u95ED\u73BB\u7483\uFF1B1 = \u6700\u901A\u900F",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(GlassSlider, { label: "\u73BB\u7483\u900F\u660E\u5EA6", value: value.alpha, onChange: (alpha) => set({ alpha }) })
        }
      ),
      glassActive ? null : /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "s-warn", children: "\u73BB\u7483\u5DF2\u5173\u95ED\uFF08\u900F\u660E\u5EA6\u4E3A 0\uFF09\uFF1A\u8868\u9762\u4E3A\u4E0D\u900F\u660E\u6750\u8D28\uFF0C\u4E0B\u65B9\u9009\u9879\u4E0D\u751F\u6548\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          gap: true,
          title: "\u78E8\u7802\u7A0B\u5EA6",
          desc: "0 = \u6E05\u73BB\u7483 \xB7 1 = \u539A\u78E8\u7802\uFF08\u5976\u767D\u96FE\u6C14\u6D53\u5EA6\uFF09",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            GlassSlider,
            {
              label: "\u78E8\u7802\u7A0B\u5EA6",
              value: value.frost,
              onChange: (frost) => set({ frost }),
              disabled: !glassActive
            }
          )
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          gap: true,
          title: "\u6DB2\u6001\u9AD8\u5EA6",
          desc: "0 = \u8D34\u5728\u9875\u9762\u4E0A \xB7 1 = \u660E\u663E\u6D6E\u8D77\uFF08\u6295\u5F71\u6DF1\u5EA6 + \u4E0A\u6CBF\u9AD8\u5149\uFF09",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            GlassSlider,
            {
              label: "\u6DB2\u6001\u9AD8\u5EA6",
              value: value.liquid,
              onChange: (liquid) => set({ liquid }),
              disabled: !glassActive
            }
          )
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u51CF\u5F31\u900F\u660E\u964D\u7EA7\uFF08\u65E0\u969C\u788D\uFF09",
          desc: "\u5F00\u542F\u540E\u73BB\u7483\u5207\u4E0D\u900F\u660E\u78E8\u7802\u5C42\uFF0C\u4FDD\u8BC1\u6B63\u6587 4.5:1 \u5BF9\u6BD4",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            Sw,
            {
              label: "\u51CF\u5F31\u900F\u660E\u964D\u7EA7",
              checked: value.reducedTransparency,
              onChange: (reducedTransparency) => set({ reducedTransparency })
            }
          )
        }
      )
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(AppearanceBackgroundCard, { bridge: bridge2, store: store2 }),
    store2.save === "error" ? /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("p", { className: "cap cap--warn", children: [
      "\u6CA1\u6709\u5199\u8FDB\u53BB\u3002",
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("button", { type: "button", className: "link", onClick: store2.retrySave, children: "\u91CD\u8BD5" })
    ] }) : null
  ] });
}
function AppearanceBackgroundCard({
  store: store2
}) {
  const { value } = store2;
  const bg = value.background;
  const scene = value.scene;
  const set = (patch) => store2.update({ ...value, ...patch });
  const setBackground = (patch) => set({ background: { ...bg, ...patch } });
  const setScene = (patch) => set({ scene: { ...scene, ...patch } });
  const solidPicker = (0, import_react34.useRef)(null);
  const tintPicker = (0, import_react34.useRef)(null);
  const imagePicker = (0, import_react34.useRef)(null);
  return /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(
    SettingCard,
    {
      title: "\u80CC\u666F\uFF08\u51B3\u5B9A\u73BB\u7483\u80FD\u900F\u51FA\u4EC0\u4E48\uFF09",
      note: "\u534A\u900F\u660E\u53EA\u662F\u524D\u63D0 \u2014\u2014 \u80CC\u540E\u5F97\u6709\u5185\u5BB9\u624D\u770B\u5F97\u89C1\u3002\u7EAF\u8272\u80CC\u666F\u4E0B\u65E0\u8BBA\u600E\u4E48\u8C03\uFF0C\u73BB\u7483\u90FD\u50CF\u4E00\u5757\u5B9E\u5FC3\u677F\u3002",
      children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
          SettingRow,
          {
            title: "\u80CC\u666F\u6A21\u5F0F",
            desc: "\u7A97\u53E3\u900F\u89C6\u4F1A\u8BA9\u6574\u4E2A\u7A97\u53E3\u80CC\u666F\u900F\u660E\uFF0C\u76F4\u63A5\u770B\u7A7F\u5230\u684C\u9762",
            control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
              "select",
              {
                className: "picker",
                "aria-label": "\u80CC\u666F\u6A21\u5F0F",
                value: bg.mode,
                onChange: (event) => setBackground({ mode: event.target.value }),
                children: BACKGROUND_MODE_CHOICES.map((choice) => /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("option", { value: choice.mode, children: choice.label }, choice.mode))
              }
            )
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
        solidVisible(bg.mode) ? /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingBlock, { children: [
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", children: "\u7EAF\u8272\u5E95" }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "s-row__desc", children: "\u5341\u9897\u6311\u597D\u7684\u5E95\u8272\uFF1A\u51B7/\u6696/\u4E2D\u6027\u3001\u6D45/\u6DF1\u90FD\u6709\u3002\u9009\u4E2D\u7ACB\u523B\u751F\u6548 \u2014\u2014 \u73BB\u7483\u80CC\u540E\u90A3\u5F20\u7CCA\u5E95\u4F1A\u6309\u65B0\u8272\u91CD\u65B0\u5408\u6210\uFF0C\u4E0D\u7528\u91CD\u542F\u3002\u60F3\u8981\u8FD9\u5341\u9897\u4EE5\u5916\u7684\u989C\u8272\uFF0C\u70B9\u300C\u9009\u4E2A\u989C\u8272\u2026\u300D\u3002" }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("div", { className: "swatches", children: SOLID_SWATCHES.map((swatch) => {
            const selected = swatch.hex.toLowerCase() === bg.solidHex.toLowerCase();
            return /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
              "button",
              {
                type: "button",
                className: "swatch",
                "aria-label": swatch.name,
                "aria-pressed": selected,
                onClick: () => setBackground({ solidHex: swatch.hex }),
                children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "swatch__fill", style: { background: swatch.hex } })
              },
              swatch.hex
            );
          }) }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { className: "btnrow", children: [
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(GlassButton, { size: "md", className: "gbtn--tight", onClick: () => solidPicker.current?.click(), children: "\u9009\u4E2A\u989C\u8272\u2026" }),
            bg.solidHex ? /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("span", { className: "cap", children: [
              "\u5F53\u524D ",
              bg.solidHex
            ] }) : /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", {}),
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("button", { type: "button", className: "textbtn", onClick: () => setBackground({ solidHex: "" }), children: "\u8DDF\u4E3B\u9898" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: solidHint(bg.solidHex) }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            "input",
            {
              ref: solidPicker,
              type: "color",
              className: "colorpick",
              tabIndex: -1,
              "aria-hidden": "true",
              value: bg.solidHex || SOLID_SWATCHES[0].hex,
              onChange: (event) => setBackground({ solidHex: event.target.value.toUpperCase() })
            }
          )
        ] }) : null,
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingBlock, { children: [
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", children: "\u9875\u9762\u8272\uFF08\u5361\u7247\u4E0E\u9875\u9762\u7684\u5E95\u8272\uFF09" }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "s-row__desc", children: "\u9875\u9762\u8272\u53EA\u51B3\u5B9A\u5361\u7247\u4E0E\u9875\u9762\u7684\u5E95\u8272\uFF1B\u80CC\u666F\u8272\u51B3\u5B9A\u73BB\u7483\u80CC\u540E\u900F\u51FA\u4EC0\u4E48\u3002\u4E24\u8005\u4E92\u4E0D\u8986\u76D6\uFF0C\u53EF\u4EE5\u5404\u6311\u5404\u7684\u3002" }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { className: "btnrow", children: [
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(GlassButton, { size: "md", className: "gbtn--tight", onClick: () => tintPicker.current?.click(), children: "\u9009\u4E2A\u989C\u8272\u2026" }),
            bg.pageTintHex ? /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("span", { className: "cap", children: [
              "\u5F53\u524D ",
              bg.pageTintHex
            ] }) : /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", {}),
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("button", { type: "button", className: "textbtn", onClick: () => setBackground({ pageTintHex: "" }), children: "\u8DDF\u4E3B\u9898" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: pageTintHint(bg.pageTintHex) }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            "input",
            {
              ref: tintPicker,
              type: "color",
              className: "colorpick",
              tabIndex: -1,
              "aria-hidden": "true",
              value: bg.pageTintHex || SOLID_SWATCHES[0].hex,
              onChange: (event) => setBackground({ pageTintHex: event.target.value.toUpperCase() })
            }
          )
        ] }),
        bg.mode === "image" ? /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
          SettingRow,
          {
            title: "\u80CC\u666F\u56FE\u7247",
            desc: bg.imageLabel,
            control: /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(import_jsx_runtime46.Fragment, { children: [
              /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(GlassButton, { size: "md", className: "gbtn--tight", onClick: () => imagePicker.current?.click(), children: "\u9009\u62E9\u56FE\u7247" }),
              /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
                "input",
                {
                  ref: imagePicker,
                  type: "file",
                  accept: "image/*",
                  className: "sr-only",
                  tabIndex: -1,
                  onChange: (event) => setBackground({ imageLabel: event.target.files?.[0]?.name ?? "\u5C1A\u672A\u9009\u62E9\u56FE\u7247" })
                }
              )
            ] })
          }
        ) : null,
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
          SettingRow,
          {
            title: "\u6D4B\u8BD5\u7248\u573A\u666F\u7279\u6548",
            desc: "\u5B9E\u4F53\u4E91\u3001\u8FD1\u666F\u96E8\u3001\u5149\u67F1\u4E0E\u5149\u5411\u3002\u9ED8\u8BA4\u5173\u95ED\uFF1B\u5F00\u542F\u540E\u4E0B\u65B9\u573A\u666F\u9009\u9879\u624D\u751F\u6548\u3002",
            control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
              Sw,
              {
                label: "\u6D4B\u8BD5\u7248\u573A\u666F\u7279\u6548",
                checked: scene.experimental,
                onChange: (experimental) => setScene({ experimental })
              }
            )
          }
        ),
        scene.experimental ? /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(import_jsx_runtime46.Fragment, { children: [
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            SettingRow,
            {
              title: "\u5929\u6C14\u573A\u666F\u53E0\u52A0",
              desc: "\u6674/\u9634/\u6BDB\u6BDB\u96E8/\u5C0F\u96E8/\u5927\u96E8\uFF0C\u96E8\u6EF4\u4F1A\u6D6E\u5728\u754C\u9762\u4E0A",
              control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u5929\u6C14\u573A\u666F\u53E0\u52A0", checked: scene.weather, onChange: (weather) => setScene({ weather }) })
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            SettingRow,
            {
              title: "\u573A\u666F\u5149\u7167\u4E0E\u5149\u67F1",
              desc: "\u5149\u7684\u65B9\u5411\u968F\u4E00\u5929\u53D8\u5316\u3001\u5F3A\u5EA6\u968F\u80FD\u89C1\u5EA6\u53D8\u5316\uFF1B\u5173\u6389\u53EA\u4FDD\u7559\u4E91\u4E0E\u5E95\u8272",
              control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u573A\u666F\u5149\u7167\u4E0E\u5149\u67F1", checked: scene.light, onChange: (light) => setScene({ light }) })
            }
          ),
          scene.weather ? /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(import_jsx_runtime46.Fragment, { children: [
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
              SettingRow,
              {
                title: "\u573A\u666F\u97F3\u6548",
                desc: "\u96E8\u58F0\u3001\u98CE\u58F0\u3001\u95EA\u7535\uFF1B\u9700\u5148\u5728\u7D20\u6750\u5E93\u5206\u914D\u97F3\u6548\u5361\u69FD",
                control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u573A\u666F\u97F3\u6548", checked: scene.audio, onChange: (audio) => setScene({ audio }) })
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
              SettingRow,
              {
                gap: true,
                title: "\u573A\u666F\u97F3\u91CF",
                control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
                  GlassSlider,
                  {
                    label: "\u573A\u666F\u97F3\u91CF",
                    width: 140,
                    value: scene.audioLevel,
                    onChange: (audioLevel) => setScene({ audioLevel })
                  }
                )
              }
            )
          ] }) : null
        ] }) : null
      ]
    }
  );
}
function SoundSection() {
  const [bgm, setBgm] = useNum(0.7);
  const [sfx, setSfx] = useNum(0.8);
  const [uiSound, setUiSound] = useBool(true);
  const [uiSoundLevel, setUiSoundLevel] = useNum(0.5);
  const [textSpeed, setTextSpeed] = useNum(1);
  const [keySfx, setKeySfx] = useStr("default");
  const [tts, setTts] = useBool(true);
  const [ttsEngine, setTtsEngine] = useStr("system");
  const [stt, setStt] = useBool(true);
  const [sttEngine, setSttEngine] = useStr("system");
  const [audioInputMode, setAudioInputMode] = useStr("internal");
  const [advancedAnalysis, setAdvancedAnalysis] = useBool(false);
  const [cameraFace, setCameraFace] = useBool(false);
  const [ttsRate, setTtsRate] = useNum(1);
  const [previewText, setPreviewText] = useStr("\u4F60\u597D\uFF0C\u6211\u662F\u6E29\u6696\u5982\u521D\u3002");
  const [voiceMode, setVoiceMode] = useStr("OpenAI \u517C\u5BB9\uFF08\u7F51\u7EDC API\uFF09");
  const [voiceUrl, setVoiceUrl] = useStr("https://api.openai.com/v1");
  const [voiceKey, setVoiceKey] = useStr("");
  const [voiceTtsModel, setVoiceTtsModel] = useStr("tts-1");
  const [voiceName, setVoiceName] = useStr("alloy");
  const [voiceSttModel, setVoiceSttModel] = useStr("whisper-1");
  const [voiceExtra, setVoiceExtra] = useStr("");
  const needsVoiceApi = ttsEngine === "api" || sttEngine === "api";
  const sovits = voiceMode === "GPT-SoVITS\uFF08\u672C\u5730/\u5C40\u57DF\u7F51\uFF09";
  return /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(import_jsx_runtime46.Fragment, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u97F3\u91CF", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(SettingRow, { title: "BGM", control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(GlassSlider, { label: "BGM", value: bgm, onChange: setBgm }) }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(SettingRow, { title: "\u97F3\u6548", control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(GlassSlider, { label: "\u97F3\u6548", value: sfx, onChange: setSfx }) }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u754C\u9762\u63D0\u793A\u97F3",
          desc: "\u6309\u94AE\u6309\u4E0B\u3001\u9009\u62E9\u786E\u8BA4\u3001\u5BFC\u5165\u5B8C\u6210\u65F6\u7684\u8F7B\u54CD\u3002\u5173\u6389\u4E0D\u5F71\u54CD\u80CC\u666F\u97F3\u4E50\u4E0E\u7D20\u6750\u97F3\u6548\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u754C\u9762\u63D0\u793A\u97F3", checked: uiSound, onChange: setUiSound })
        }
      ),
      uiSound ? /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u63D0\u793A\u97F3\u97F3\u91CF",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(GlassSlider, { label: "\u63D0\u793A\u97F3\u97F3\u91CF", value: uiSoundLevel, onChange: setUiSoundLevel })
        }
      ) : null
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u6587\u5B57\u4E0E\u97F3\u6548", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u6587\u672C\u901F\u5EA6",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(RangeSlider, { label: "\u6587\u672C\u901F\u5EA6", min: 0.5, max: 2, step: 0.1, value: textSpeed, onChange: setTextSpeed })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        PickRow,
        {
          title: "\u6309\u952E\u97F3\u6548",
          desc: "\u70B9\u51FB\u6309\u94AE\u65F6\u7684\u53CD\u9988\u97F3",
          value: keySfx,
          options: ["default", "soft", "typewriter", "none"],
          onChange: setKeySfx,
          width: 120
        }
      )
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u8BED\u97F3", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u6717\u8BFB\u56DE\u590D",
          desc: "\u6536\u5230\u56DE\u590D\u65F6\u7528\u8BED\u97F3\u8BFB\u51FA\u6765",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u6717\u8BFB\u56DE\u590D", checked: tts, onChange: setTts })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        PickRow,
        {
          title: "\u6717\u8BFB\u5F15\u64CE",
          desc: "system=\u7CFB\u7EDF\u81EA\u5E26\u8BED\u97F3\uFF0Capi=\u5916\u90E8 TTS API",
          value: ttsEngine,
          options: ["system", "api", "local"],
          onChange: setTtsEngine,
          width: 100
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u8BED\u97F3\u8F93\u5165",
          desc: "\u663E\u793A\u8BED\u97F3\u8F93\u5165\u6309\u94AE\uFF08\u9700\u9EA6\u514B\u98CE\u6743\u9650\uFF09",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u8BED\u97F3\u8F93\u5165", checked: stt, onChange: setStt })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        PickRow,
        {
          title: "\u8BC6\u522B\u5F15\u64CE",
          desc: "system=\u7CFB\u7EDF\u81EA\u5E26\uFF0Clocal=\u672C\u5730\u4E0B\u8F7D\u6A21\u578B\uFF0Capi=\u5916\u90E8 STT API",
          value: sttEngine,
          options: ["system", "api", "local"],
          onChange: setSttEngine,
          width: 100
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        PickRow,
        {
          title: "\u97F3\u9891\u8F93\u5165\u6A21\u5F0F",
          desc: "internal=\u5148\u5185\u90E8\u8BC6\u522B\u6210\u6587\u5B57\uFF1Bdirect=\u97F3\u9891\u76F4\u9001\u652F\u6301\u542C\u97F3\u9891\u7684\u5927\u6A21\u578B",
          value: audioInputMode,
          options: ["internal", "direct"],
          onChange: setAudioInputMode,
          width: 100
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u9AD8\u7EA7\u5206\u6790",
          desc: "\u628A\u4EBA\u8138\u8BC6\u522B/\u8BED\u97F3\u97F5\u5F8B\u7684\u60C5\u7EEA\u4E0E\u5E74\u9F84\u4EA4\u5927\u6A21\u578B\u6DF1\u5EA6\u89E3\u8BFB\uFF08\u9700\u914D\u7F6E API\uFF09",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u9AD8\u7EA7\u5206\u6790", checked: advancedAnalysis, onChange: setAdvancedAnalysis })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u6444\u50CF\u5934\u4EBA\u8138\u8BC6\u522B",
          desc: "\u5141\u8BB8\u672C\u5730\u4EBA\u8138\u6A21\u578B\u5206\u6790\u60C5\u7EEA\u4E0E\u5E74\u9F84\uFF0C\u6570\u636E\u53EA\u5728\u672C\u673A\u5904\u7406",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u6444\u50CF\u5934\u4EBA\u8138\u8BC6\u522B", checked: cameraFace, onChange: setCameraFace })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u53EA\u8BFB\uFF1A\u53EA\u4EA7\u51FA\u60C5\u7EEA / \u5E74\u9F84 / \u662F\u5426\u5728\u573A\u8FD9\u7C7B\u4FE1\u606F\uFF0C\u4E0D\u6539\u4EFB\u4F55\u6570\u503C\u3001\u4E0D\u63A8\u5267\u60C5\u3001\u4E0D\u89E6\u53D1\u52A8\u4F5C\uFF1B\u56FE\u50CF\u53EA\u5728\u672C\u673A\uFF0C\u4E0D\u4E0A\u4F20\u3001\u4E0D\u5E38\u9A7B\u4FDD\u5B58\u3002AI \u60F3\u300C\u770B\u4E00\u773C\u300D\u65F6\u5FC5\u987B\u7ECF\u4F60\u6309\u4E0B\u5FEB\u95E8\uFF0C\u5BF9\u8BDD\u5F00\u59CB\u65F6\u4E5F\u53EA\u5728\u6709\u8DB3\u591F\u65B0\u7684\u753B\u9762\u65F6\u624D\u5224\u65AD\u4E00\u6B21\u3002" }),
      sttEngine === "local" ? /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingBlock, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", children: "\u672C\u5730\u8BC6\u522B\u6A21\u578B" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
          PickRow,
          {
            title: "\u672C\u5730\u6A21\u578B",
            value: "\u672A\u9009\u62E9\u672C\u5730\u6A21\u578B",
            options: ["\u672A\u9009\u62E9\u672C\u5730\u6A21\u578B"],
            onChange: () => void 0,
            width: 240,
            label: "\u672C\u5730\u6A21\u578B"
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u672A\u9009\u62E9\u672C\u5730\u6A21\u578B" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "flex", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u4E0B\u8F7D\u6A21\u578B" }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u5220\u9664\u6A21\u578B" })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "local=\u79BB\u7EBF\u8BC6\u522B\uFF08\u9700\u5148\u4E0B\u8F7D\u6A21\u578B\uFF09\uFF1Bapi=\u5916\u90E8\u670D\u52A1\uFF08\u7535\u8111\u7AEF\u5F55\u97F3 8 \u79D2\u540E\u8BC6\u522B\uFF09" })
      ] }) : null,
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u6717\u8BFB\u8BED\u901F",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(RangeSlider, { label: "\u6717\u8BFB\u8BED\u901F", min: 0.5, max: 2, step: 0.1, value: ttsRate, onChange: setTtsRate })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingBlock, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", children: "\u8BD5\u542C\u5F53\u524D\u97F3\u8272" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Field, { value: previewText, onChange: setPreviewText, placeholder: "\u8F93\u5165\u4E00\u6BB5\u8BDD\u8BD5\u542C\uFF0C\u5982\uFF1A\u4F60\u597D\uFF0C\u6211\u662F\u6E29\u6696\u5982\u521D\u3002", label: "\u8BD5\u542C\u6587\u672C" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "cap", children: "\u6309\u5F53\u524D\u8BBE\u7F6E\u5408\u6210\u8BD5\u542C\uFF1A\u7CFB\u7EDF\u5F15\u64CE\u8D70\u7CFB\u7EDF\u8BED\u97F3\uFF1BAPI \u5F15\u64CE\u8D70\u5916\u90E8\u670D\u52A1" }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u8BD5\u542C" })
        ] })
      ] }),
      needsVoiceApi ? /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingBlock, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", children: "API \u8BED\u97F3\u914D\u7F6E" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
          PickRow,
          {
            title: "\u670D\u52A1\u7C7B\u578B",
            desc: "OpenAI \u517C\u5BB9\u8D70\u7F51\u7EDC API\uFF1BGPT-SoVITS \u53EF\u8FDE\u672C\u5730\u6216\u5C40\u57DF\u7F51\u670D\u52A1",
            value: voiceMode,
            options: ["OpenAI \u517C\u5BB9\uFF08\u7F51\u7EDC API\uFF09", "GPT-SoVITS\uFF08\u672C\u5730/\u5C40\u57DF\u7F51\uFF09"],
            onChange: setVoiceMode,
            width: 150
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "API \u5730\u5740", value: voiceUrl, onChange: setVoiceUrl, placeholder: "https://api.openai.com/v1" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "API \u5BC6\u94A5", value: voiceKey, onChange: setVoiceKey, placeholder: "sk-\u2026", type: "password" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "TTS \u6A21\u578B", value: voiceTtsModel, onChange: setVoiceTtsModel, placeholder: "tts-1" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
          FieldRow,
          {
            title: "TTS \u97F3\u8272",
            value: voiceName,
            onChange: setVoiceName,
            placeholder: "alloy / echo / nova / shimmer"
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "STT \u6A21\u578B", value: voiceSttModel, onChange: setVoiceSttModel, placeholder: "whisper-1" }),
        sovits ? /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "\u8BF4\u8BDD\u4EBA\u7F16\u53F7", value: voiceExtra, onChange: setVoiceExtra, placeholder: "0=\u9ED8\u8BA4\u89D2\u8272" }) : null,
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("div", { children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u6D4B\u8BD5\u8FDE\u63A5" }) })
      ] }) : null
    ] })
  ] });
}
var AI_PRESETS = [
  "\u901A\u7528\uFF08OpenAI\u517C\u5BB9\uFF09",
  "DeepSeek",
  "MiMo\uFF08\u5C0F\u7C73\uFF09",
  "Kimi\uFF08\u6708\u4E4B\u6697\u9762\uFF09",
  "\u8C46\u5305\uFF08\u706B\u5C71\u65B9\u821F\uFF09",
  "\u667A\u8C31 AI",
  "\u7845\u57FA\u6D41\u52A8",
  "\u901A\u4E49\u5343\u95EE\uFF08DashScope\uFF09",
  "\u81EA\u5B9A\u4E49"
];
function AiSection() {
  const [preset, setPreset] = useStr("\u81EA\u5B9A\u4E49");
  const [aiUrl, setAiUrl] = useStr("https://api.openai.com/v1/chat/completions");
  const [aiKey, setAiKey] = useStr("");
  const [aiModel, setAiModel] = useStr("gpt-4o");
  const [temperature, setTemperature] = useNum(0.8);
  const [maxTokens, setMaxTokens] = useNum(500);
  const [deepThink, setDeepThink] = useBool(false);
  const [deepModel, setDeepModel] = useStr("");
  const [smallFirst, setSmallFirst] = useBool(false);
  const [smallUrl, setSmallUrl] = useStr("");
  const [smallKey, setSmallKey] = useStr("");
  const [smallModel, setSmallModel] = useStr("");
  const [autoThink, setAutoThink] = useBool(false);
  const [planFlow, setPlanFlow] = useBool(false);
  const [planMaxSteps, setPlanMaxSteps] = useNum(8);
  const [advisor, setAdvisor] = useBool(false);
  const [advisorFreq, setAdvisorFreq] = useStr("never");
  const [visionUrl, setVisionUrl] = useStr("");
  const [visionKey, setVisionKey] = useStr("");
  const [visionModel, setVisionModel] = useStr("");
  const [embedding, setEmbedding] = useBool(true);
  const [embeddingUrl, setEmbeddingUrl] = useStr("");
  const [embeddingKey, setEmbeddingKey] = useStr("");
  const [embeddingModel, setEmbeddingModel] = useStr("");
  const [embeddingDims, setEmbeddingDims] = useStr("0");
  const [rotation, setRotation] = useBool(true);
  const [timeout, setTimeoutSeconds] = useStr("60");
  const [newProviderName, setNewProviderName] = useStr("");
  const [newProviderModel, setNewProviderModel] = useStr("");
  const [newProviderUrl, setNewProviderUrl] = useStr("");
  const [newProviderKeys, setNewProviderKeys] = useStr("");
  const [newProviderTimeout, setNewProviderTimeout] = useStr("");
  const [smartSaving, setSmartSaving] = useBool(true);
  const [priceModel, setPriceModel] = useStr("");
  const [priceIn, setPriceIn] = useStr("");
  const [priceOut, setPriceOut] = useStr("");
  const [chartMode, setChartMode] = useStr("\u6761\u5F62\u56FE");
  const [imageGen, setImageGen] = useBool(false);
  const [imageUrl, setImageUrl] = useStr("https://api.openai.com/v1");
  const [imageKey, setImageKey] = useStr("");
  const [imageModel, setImageModel] = useStr("gpt-image-1");
  const [imageSize, setImageSize] = useStr("1024x1024");
  const [paramsFormat, setParamsFormat] = useStr("openai_flat");
  const [paramsJson, setParamsJson] = useStr("");
  const [thinkBar, setThinkBar] = useBool(false);
  const [quickButtons, setQuickButtons] = useBool(false);
  const [thinkTiers, setThinkTiers] = useNum(3);
  const [thinkLabels, setThinkLabels] = useStr("\u8F7B\u5EA6,\u6807\u51C6,\u6DF1\u5EA6");
  const [thinkKey, setThinkKey] = useStr("reasoning_effort");
  const [thinkValues, setThinkValues] = useStr("low,medium,high");
  return /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(import_jsx_runtime46.Fragment, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "AI \u5BF9\u8BDD", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(PickRow, { title: "API \u9884\u8BBE", value: preset, options: AI_PRESETS, onChange: setPreset, width: 240, label: "API \u9884\u8BBE" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        FieldRow,
        {
          title: "API \u5730\u5740",
          value: aiUrl,
          onChange: setAiUrl,
          placeholder: "https://api.openai.com/v1/chat/completions",
          width: 380
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "API \u5BC6\u94A5", value: aiKey, onChange: setAiKey, placeholder: "sk-\u2026", type: "password", width: 320 }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u6A21\u578B",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("span", { style: { display: "flex", gap: "var(--w-space-2)", alignItems: "center" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Select, { value: aiModel, options: [{ value: aiModel, label: aiModel }], onChange: setAiModel, label: "\u6A21\u578B" }),
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u68C0\u6D4B" })
          ] })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(SettingRow, { title: "\u6E29\u5EA6", control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(GlassSlider, { label: "\u6E29\u5EA6", value: temperature, onChange: setTemperature }) }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u6700\u5927 Token",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(RangeSlider, { label: "\u6700\u5927 Token", min: 100, max: 2e3, step: 100, value: maxTokens, onChange: setMaxTokens })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u6DF1\u5EA6\u601D\u8003",
          desc: "\u590D\u6742\u5267\u60C5\u65F6\u4F7F\u7528\u66F4\u5F3A\u7684\u6A21\u578B",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u6DF1\u5EA6\u601D\u8003", checked: deepThink, onChange: setDeepThink })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "\u6DF1\u5EA6\u6A21\u578B", value: deepModel, onChange: setDeepModel, placeholder: "o3 / \u63A8\u7406\u6A21\u578B", width: 320 }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Divider, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", children: "\u5C0F\u6A21\u578B\u4F18\u5148\uFF08\u53EF\u9009\uFF09" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "s-row__desc", children: "\u5F00\u542F\u540E\u6240\u6709\u5BF9\u8BDD\u5148\u4EA4\u7ED9\u5C0F\u6A21\u578B\uFF0C\u5904\u7406\u4E0D\u6765\u518D\u5347\u7EA7\u5230\u5927\u6A21\u578B\u3002\u5C0F\u6A21\u578B\u8981\u52A8\u6E38\u620F\u5374\u53C8\u6CA1\u6709\u5DE5\u5177\u65F6\uFF0C\u4F1A\u6309\u63D0\u793A\u76F4\u63A5\u8BF4\u300C\u6211\u9700\u8981\u4F7F\u7528\u5927\u6A21\u578B\u300D\uFF0C\u7531\u5927\u6A21\u578B\u63A5\u624B\u2014\u2014\u6240\u4EE5\u5973\u4EC6\u6865\u8FDE\u7740\u65F6\u4E5F\u7167\u6837\u80FD\u7528\uFF0C\u4E0D\u4F1A\u628A\u5DE5\u5177\u4EA4\u4E92\u5F04\u574F\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u5F00\u542F\u5C0F\u6A21\u578B\u4F18\u5148",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u5F00\u542F\u5C0F\u6A21\u578B\u4F18\u5148", checked: smallFirst, onChange: setSmallFirst })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        FieldRow,
        {
          title: "\u5C0F\u6A21\u578B\u63A5\u53E3",
          value: smallUrl,
          onChange: setSmallUrl,
          placeholder: "\u7559\u7A7A\u5219\u6CBF\u7528\u4E0A\u9762\u7684 AI \u5730\u5740",
          width: 340
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        FieldRow,
        {
          title: "\u5C0F\u6A21\u578B\u5BC6\u94A5",
          value: smallKey,
          onChange: setSmallKey,
          placeholder: "\u7559\u7A7A\u5219\u6CBF\u7528\u4E0A\u9762\u7684 AI \u5BC6\u94A5",
          type: "password",
          width: 340
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        FieldRow,
        {
          title: "\u5C0F\u6A21\u578B\u540D",
          value: smallModel,
          onChange: setSmallModel,
          placeholder: "\u4E00\u4E2A\u4FBF\u5B9C/\u5FEB\u7684\u6A21\u578B\u540D\uFF0C\u5982 qwen-flash / glm-4-flash",
          width: 340
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Divider, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", children: "AI \u81EA\u4E3B\u80FD\u529B\uFF08\u5B9E\u9A8C\uFF09" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "s-row__desc", children: "\u8BA9 AI \u5728\u5BF9\u8BDD\u4E2D\u81EA\u884C\u51B3\u5B9A\u601D\u8003\u6DF1\u5EA6\u3001\u6216\u53D1\u8D77\u591A\u6B65\u590D\u5408\u4EFB\u52A1\u3002\u4E24\u7C7B\u80FD\u529B\u9ED8\u8BA4\u5173\u95ED\uFF1B\u5F00\u542F\u540E AI \u53EF\u901A\u8FC7\u5DE5\u5177\u4E34\u65F6\u63D0\u5347\u601D\u8003\u6863\u4F4D\u3001\u6216\u81EA\u4E3B\u7F16\u6392\u300C\u63A2\u6D4B\u2192\u91C7\u96C6\u2192\u5EFA\u9020\u2192\u9A8C\u8BC1\u300D\u591A\u6B65\u6D41\u7A0B\u3002\u591A\u6B65\u4E0A\u9650\u7528\u4E8E\u9632\u6B62\u5931\u63A7\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "AI \u81EA\u4E3B\u9009\u62E9\u601D\u8003\u6863\u4F4D\uFF08\u542B\u6DF1\u5EA6\u601D\u8003\uFF09",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "AI \u81EA\u4E3B\u9009\u62E9\u601D\u8003\u6863\u4F4D", checked: autoThink, onChange: setAutoThink })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "AI \u81EA\u4E3B\u53D1\u8D77\u591A\u6B65\u8BA1\u5212",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "AI \u81EA\u4E3B\u53D1\u8D77\u591A\u6B65\u8BA1\u5212", checked: planFlow, onChange: setPlanFlow })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u591A\u6B65\u4E0A\u9650",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(RangeSlider, { label: "\u591A\u6B65\u4E0A\u9650", min: 1, max: 32, step: 1, value: planMaxSteps, onChange: setPlanMaxSteps })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Divider, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", children: "\u8BBE\u7F6E\u8C03\u8282 AI\uFF08F08\uFF09" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "s-row__desc", children: "\u7528\u4E00\u6BB5\u4E0E\u89D2\u8272\u4EBA\u683C\u3001\u8BB0\u5FC6\u5B8C\u5168\u9694\u79BB\u7684\u4E34\u65F6 AI \u8C03\u7528\uFF0C\u5206\u6790\u5F53\u524D\u8BBE\u7F6E\u5E76\u7ED9\u4F18\u5316\u5EFA\u8BAE\uFF1B\u4E0D\u5199\u5165\u4EFB\u4F55\u89D2\u8272\u8BB0\u5FC6\u3002\u9ED8\u8BA4\u300C\u6C38\u4E0D\u300D\u6253\u6270\uFF0C\u5F00\u542F\u540E\u6309\u4F60\u8BBE\u5B9A\u7684\u9891\u7387\u8FD0\u884C\uFF08\u6BCF\u5929 / \u6BCF\u4E09\u5929 / \u6BCF\u5468\uFF09\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u542F\u7528\u8BBE\u7F6E\u5EFA\u8BAE AI",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u542F\u7528\u8BBE\u7F6E\u5EFA\u8BAE AI", checked: advisor, onChange: setAdvisor })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        PickRow,
        {
          title: "\u8FD0\u884C\u9891\u7387",
          value: advisorFreq,
          options: ["never", "daily", "3days", "weekly"],
          onChange: setAdvisorFreq,
          width: 190
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(BtnRow, { children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u7ACB\u5373\u5206\u6790" }) }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Divider, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", children: "\u8BC6\u56FE\u6A21\u578B\uFF08\u53EF\u9009\uFF09" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "s-row__desc", children: "\u72EC\u7ACB\u914D\u7F6E\u4E00\u4E2A\u652F\u6301\u770B\u56FE\u7684\u6A21\u578B\uFF1B\u914D\u597D\u540E\u300C\u8868\u60C5\u5305\u5E93\u300D\u91CC\u53EF\u4EE5\u4E00\u952E\u8BA9 AI \u5199\u63CF\u8FF0\u3002\u4E0D\u586B\u4E5F\u80FD\u7528\uFF1A\u4F1A\u4F18\u5148\u627E\u5382\u5546\u91CC\u6807\u6CE8\u4E86\u300C\u80FD\u770B\u56FE\u300D\u7684\u6A21\u578B\uFF0C\u90FD\u6CA1\u6709\u5C31\u76F4\u63A5\u7528\u5927\u6A21\u578B\u515C\u5E95\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        FieldRow,
        {
          title: "\u8BC6\u56FE\u7AEF\u70B9",
          value: visionUrl,
          onChange: setVisionUrl,
          placeholder: "https://\u2026/v1/chat/completions",
          width: 340
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "\u8BC6\u56FE Key", value: visionKey, onChange: setVisionKey, placeholder: "sk-\u2026", type: "password", width: 320 }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        FieldRow,
        {
          title: "\u8BC6\u56FE\u6A21\u578B",
          value: visionModel,
          onChange: setVisionModel,
          placeholder: "gpt-4o-mini / qwen-vl-plus / glm-4v \u2026",
          width: 320
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Divider, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u91CF\u5316\u6A21\u578B\uFF08embedding\uFF09",
          desc: "\u4E13\u95E8\u7684\u77E5\u8BC6\u5E93\u7BA1\u7406\u5458\uFF1A\u628A\u8BCD\u8BED\u548C\u53E5\u5B50\u6362\u6210\u6570\u503C\uFF0C\u8FD1\u4F3C\u7684\u8BF4\u6CD5\u6570\u503C\u5C31\u8FD1\uFF0C\u300C\u8C01\u548C\u5B83\u6700\u50CF\u300D\u5C31\u80FD\u76F4\u63A5\u7B97\u51FA\u6765\u3002\u5173\u7CFB\u7F51\u7684\u8FD1\u90BB\u3001\u6309\u6570\u503C\u8865\u7EBF\u548C\u6309\u6570\u503C\u641C\u8BB0\u5FC6\u90FD\u4F18\u5148\u95EE\u5B83\uFF1B\u6CA1\u914D\u7F6E\u6216\u989D\u5EA6\u7528\u5B8C\u5C31\u9000\u56DE\u672C\u5730\u5171\u73B0\u7EDF\u8BA1\uFF08\u4E0D\u82B1\u94B1\uFF0C\u754C\u9762\u4F1A\u8BF4\u662F\u54EA\u6761\u8DEF\u7B97\u7684\uFF09\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u91CF\u5316\u6A21\u578B", checked: embedding, onChange: setEmbedding })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        FieldRow,
        {
          title: "\u91CF\u5316\u7AEF\u70B9",
          value: embeddingUrl,
          onChange: setEmbeddingUrl,
          placeholder: "\u7559\u7A7A\u5219\u590D\u7528\u4E0A\u9762\u7684\u5BF9\u8BDD\u7AEF\u70B9\uFF08\u81EA\u52A8\u6362\u6210 /embeddings\uFF09",
          width: 380
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        FieldRow,
        {
          title: "\u91CF\u5316 Key",
          value: embeddingKey,
          onChange: setEmbeddingKey,
          placeholder: "sk-\u2026\uFF08\u7559\u7A7A\u5219\u590D\u7528\u5BF9\u8BDD\u5BC6\u94A5\u4E0E\u5382\u5546\u5BC6\u94A5\u6C60\uFF09",
          type: "password",
          width: 380
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        FieldRow,
        {
          title: "\u91CF\u5316\u6A21\u578B",
          value: embeddingModel,
          onChange: setEmbeddingModel,
          placeholder: "text-embedding-3-small / BAAI/bge-m3 \u2026\uFF08\u7A7A = \u53EA\u7528\u672C\u5730\u7EDF\u8BA1\uFF09",
          width: 420
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        PickRow,
        {
          title: "\u5411\u91CF\u7EF4\u5EA6",
          value: embeddingDims,
          options: ["0", "256", "512", "768", "1024", "1536", "2048"],
          onChange: setEmbeddingDims,
          width: 150
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "0 = \u7531\u6A21\u578B\u81EA\u5DF1\u5B9A\u3002\u6362\u6A21\u578B\u6216\u6362\u7EF4\u5EA6\u7B49\u4E8E\u6362\u4E00\u5957\u5411\u91CF\uFF0C\u7B97\u8FC7\u7684\u4F1A\u91CD\u65B0\u7B97\u4E00\u6B21\uFF08\u540C\u6A21\u578B\u540C\u6587\u672C\u53EA\u7B97\u4E00\u6B21\uFF0C\u4E4B\u540E\u4E0D\u91CD\u590D\u82B1\u94B1\uFF09\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Divider, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u591A\u7AEF\u70B9\u8F6E\u6362\uFF08\u4F9B\u5E94\u5546 / AI / \u5BC6\u94A5\uFF09",
          desc: "\u4FDD\u5B58\u591A\u6761\u7AEF\u70B9\uFF08\u4E00\u6761 = \u4F9B\u5E94\u5546 + \u67D0\u6B3E AI + \u591A\u679A\u5BC6\u94A5\uFF09\u3002\u5F00\u542F\u540E\u5BC6\u94A5\u4E0E\u7AEF\u70B9\u81EA\u52A8\u8F6E\u6D41\u4F7F\u7528\uFF1B\u67D0\u6761\u5931\u7075\u5C31\u6362\u4E0B\u4E00\u6761\uFF0C\u4FDD\u8BC1\u5BF9\u8BDD\u4E0D\u95F4\u65AD\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u591A\u7AEF\u70B9\u8F6E\u6362", checked: rotation, onChange: setRotation })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        PickRow,
        {
          title: "\u8BF7\u6C42\u8D85\u65F6\uFF08\u79D2\uFF09",
          desc: "\u5168\u5C40\u9ED8\u8BA4\uFF1B\u67D0\u6761\u7AEF\u70B9\u9700\u8981\u66F4\u4E45\u65F6\uFF0C\u5728\u4E0B\u9762\u90A3\u6761\u7AEF\u70B9\u7684\u300C\u8D85\u65F6\u300D\u91CC\u5355\u72EC\u586B",
          value: timeout,
          options: ["10", "15", "25", "40", "60", "120", "180"],
          onChange: setTimeoutSeconds,
          width: 90
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "repeat(3, 1fr)", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u5382\u5546\u7BA1\u7406\u9875\u9762 \u2192" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "AI \u6A21\u578B\u7BA1\u7406\u9875\u9762 \u2192" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u6A21\u578B\u90E8\u7F72\u5411\u5BFC \u2192" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        Empty,
        {
          title: "\u8FD8\u6CA1\u6709\u767B\u8BB0\u7684\u5382\u5546",
          reason: "\u4E00\u5BB6\u5382\u5546 = \u7AEF\u70B9 + \u5BC6\u94A5 + \u591A\u6B3E\u6A21\u578B\uFF0C\u73B0\u5728\u5217\u8868\u91CC\u662F\u7A7A\u7684\u3002",
          hint: "\u5148\u628A\u4E0B\u9762\u7684\u540D\u5B57\u3001\u9996\u6B3E\u6A21\u578B\u548C\u7AEF\u70B9\u586B\u597D\uFF0C\u518D\u70B9\u300C\uFF0B\u6DFB\u52A0\u5382\u5546\u300D\u3002"
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Field, { value: newProviderName, onChange: setNewProviderName, placeholder: "\u5382\u5546\u540D\uFF08\u5982\uFF1A\u7845\u57FA\u6D41\u52A8\uFF09", label: "\u5382\u5546\u540D" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
          Field,
          {
            value: newProviderModel,
            onChange: setNewProviderModel,
            placeholder: "\u9996\u6B3E\u6A21\u578B\uFF08\u5982\uFF1Adeepseek-chat\uFF09",
            label: "\u9996\u6B3E\u6A21\u578B"
          }
        )
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        Field,
        {
          value: newProviderUrl,
          onChange: setNewProviderUrl,
          placeholder: "\u7AEF\u70B9\uFF08https://\u2026/v1/chat/completions\uFF09",
          label: "\u7AEF\u70B9"
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 100px", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Field, { value: newProviderKeys, onChange: setNewProviderKeys, placeholder: "\u5BC6\u94A5\uFF08\u4E00\u884C\u4E00\u679A\uFF0C\u591A\u679A\u8F6E\u6D41\u7528\uFF09", label: "\u5BC6\u94A5" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Field, { value: newProviderTimeout, onChange: setNewProviderTimeout, placeholder: "\u8D85\u65F6\u79D2", label: "\u8D85\u65F6\u79D2" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\uFF0B\u6DFB\u52A0\u5382\u5546" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u7528\u5F53\u524D\u914D\u7F6E\u9884\u586B" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Divider, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", children: "\u82B1\u8D39\u4E0E\u7701\u94B1" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u667A\u80FD\u7701\u94B1",
          desc: "\u5F00\u542F\u540E\u6298\u53E0\u5386\u53F2\u524D\u5148\u9884\u6D4B\u300C\u73B0\u5728\u603B\u7ED3 / \u4E0D\u603B\u7ED3\u300D\u54EA\u4E2A\u66F4\u7701\uFF0C\u5E76\u8BC6\u522B\u7F16\u7A0B\u529E\u516C\u573A\u666F\u6539\u7528\u8F7B\u603B\u7ED3\uFF1B\u5173\u95ED\u5219\u6062\u590D\u300C\u5230\u4E0A\u9650\u5C31\u603B\u7ED3\u300D\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u667A\u80FD\u7701\u94B1", checked: smartSaving, onChange: setSmartSaving })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 1fr 1fr auto", gap: "var(--w-space-1)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Field, { value: priceModel, onChange: setPriceModel, placeholder: "\u6A21\u578B\u540D", label: "\u6A21\u578B\u540D" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Field, { value: priceIn, onChange: setPriceIn, placeholder: "\u8F93\u5165\u4EF7/\u767E\u4E07", label: "\u8F93\u5165\u4EF7" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Field, { value: priceOut, onChange: setPriceOut, placeholder: "\u8F93\u51FA\u4EF7/\u767E\u4E07", label: "\u8F93\u51FA\u4EF7" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u4FDD\u5B58\u5355\u4EF7" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u5355\u4EF7\u53EF\u4EE5\u53EA\u586B\u4E00\u4FA7\uFF08\u53E6\u4E00\u4FA7\u6309 0 \u8BA1\uFF09\uFF0C\u4E0D\u5FC5\u6BCF\u4E2A\u6A21\u578B\u90FD\u586B\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(BtnRow, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u5237\u65B0\u7528\u91CF / \u82B1\u8D39" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u6309\u5F53\u524D\u4E0A\u9650\u8BD5\u7B97" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u7528\u91CF\u7EDF\u8BA1\u2026" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        "div",
        {
          style: {
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            minHeight: 190,
            padding: 6,
            borderRadius: 10,
            background: "var(--w-surface-tint)",
            border: "1px solid var(--w-hairline)"
          },
          children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            Empty,
            {
              title: "\u8FD8\u6CA1\u6709\u7528\u91CF",
              reason: "\u8FD9\u4EFD\u8BB0\u5F55\u5728\u540E\u7AEF\uFF0CWeb \u9884\u89C8\u91CC\u8BFB\u4E0D\u5230\u771F\u5B9E\u7528\u91CF\u3002",
              hint: "\u63A5\u4E0A\u540E\u53F0\u7684\u7528\u91CF\u8868\u4E4B\u540E\uFF0C\u8FD9\u91CC\u4F1A\u6309\u5929\u753B\u51FA\u82B1\u8D39\u4E0E token\u3002"
            }
          )
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(BtnRow, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { onClick: () => setChartMode(chartMode === "\u6761\u5F62\u56FE" ? "\u6298\u7EBF\u56FE" : "\u6761\u5F62\u56FE"), children: chartMode }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u5BFC\u51FA CSV" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", style: { fontWeight: 700 }, children: "\u4EF7\u94B1\u8868\uFF08\u6BCF\u767E\u4E07 token\uFF09" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u8FD8\u6CA1\u6709\u4FDD\u5B58\u5355\u4EF7\uFF0C\u8868\u91CC\u662F\u7A7A\u7684\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", style: { fontWeight: 700, marginTop: 6 }, children: "token \u8868" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u8FD8\u6CA1\u6709\u7528\u91CF\uFF0C\u8868\u91CC\u662F\u7A7A\u7684\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", style: { fontWeight: 700, marginTop: 6 }, children: "\u82B1\u94B1\u6392\u884C\u699C" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u8FD8\u6CA1\u6709\u82B1\u8D39\u8BB0\u5F55\uFF0C\u6392\u884C\u699C\u662F\u7A7A\u7684\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", style: { fontWeight: 700, marginTop: 6 }, children: "token \u6392\u884C\u699C" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u8FD8\u6CA1\u6709 token \u8BB0\u5F55\uFF0C\u6392\u884C\u699C\u662F\u7A7A\u7684\u3002" })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "AI \u7ED8\u753B\uFF08\u88C5\u626E / \u80CC\u666F / \u7ACB\u7ED8\uFF09", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u542F\u7528\u56FE\u7247\u751F\u6210",
          desc: "\u7528 OpenAI \u517C\u5BB9\u7684 /images/generations \u63A5\u53E3\u751F\u6210\u88C5\u626E\u3001\u573A\u666F\u80CC\u666F\u4E0E\u89D2\u8272\u7ACB\u7ED8\uFF1B\u751F\u6210\u7ED3\u679C\u843D\u5728\u672C\u5730 generated \u76EE\u5F55\uFF0C\u53EF\u518D\u5BFC\u5165\u7D20\u6750\u5E93\u3002\u5173\u95ED\u65F6\u4E0D\u82B1\u94B1\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u542F\u7528\u56FE\u7247\u751F\u6210", checked: imageGen, onChange: setImageGen })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "\u63A5\u53E3\u5730\u5740", value: imageUrl, onChange: setImageUrl, placeholder: "https://api.openai.com/v1", width: 340 }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        FieldRow,
        {
          title: "\u5BC6\u94A5",
          value: imageKey,
          onChange: setImageKey,
          placeholder: "\u53EA\u5B58\u672C\u5730\u8BBE\u7F6E\uFF0C\u4E0D\u8FDB\u4E91\u5B58\u6863",
          type: "password",
          width: 340
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "\u6A21\u578B", value: imageModel, onChange: setImageModel, placeholder: "gpt-image-1 / flux / sd\u2026", width: 320 }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "\u5C3A\u5BF8", value: imageSize, onChange: setImageSize, placeholder: "1024x1024", width: 200 })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u81EA\u5B9A\u4E49 AI \u53C2\u6570\uFF08\u53EF\u9009\uFF09", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "s-row__desc", children: "\u4EE5 JSON \u5BF9\u8C61\u586B\u5199\u989D\u5916\u8BF7\u6C42\u53C2\u6570\uFF08\u5982 reasoning_effort\u3001top_k\uFF09\uFF0C\u4F1A\u6309\u6240\u9009\u683C\u5F0F\u5E76\u5165\u53D1\u7ED9\u6A21\u578B\u7684\u8BF7\u6C42\u4F53\u3002\u7559\u7A7A\u5219\u4E0D\u9644\u52A0\uFF1B\u7ED3\u6784\u9519\u8BEF\u4F1A\u5728\u65E5\u5FD7\u91CC\u8DF3\u8FC7\uFF0C\u4E0D\u5F71\u54CD\u5BF9\u8BDD\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        PickRow,
        {
          title: "\u53C2\u6570\u683C\u5F0F",
          value: paramsFormat,
          options: ["openai_flat", "nested_by_role", "vendor_ext", "widget_binding"],
          onChange: setParamsFormat,
          width: 220
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingBlock, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", children: "\u53C2\u6570 JSON" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Field, { value: paramsJson, onChange: setParamsJson, placeholder: "\u7559\u7A7A\u8868\u793A\u4E0D\u9644\u52A0\u4EFB\u4F55\u53C2\u6570", multiline: true, label: "\u53C2\u6570 JSON" })
      ] })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u601D\u8003\u6863\u4F4D\uFF08\u601D\u8003\u6761 + \u5FEB\u901F\u6309\u94AE\uFF09", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "s-row__desc", children: "\u4E3A\u6A21\u578B\u81EA\u914D\u82E5\u5E72\u6863\u63A8\u7406\u5F3A\u5EA6\uFF0C\u5BF9\u8BDD\u4E2D\u8F93\u5165\u6761\u4E0A\u65B9\u4F1A\u51FA\u73B0\u5207\u6362\u6309\u94AE\u3001\u5E76\u5728\u601D\u8003\u65F6\u663E\u793A\u8FDB\u5EA6\u6761\u3002\u7B2C 0 \u6863\u6C38\u8FDC\u4E3A\u300C\u5173\u95ED\u300D\u3002\u5F53\u524D\u6863\u4F4D\u53EF\u5728\u5BF9\u8BDD\u91CC\u5373\u65F6\u5207\u6362\u5E76\u81EA\u52A8\u4FDD\u5B58\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u663E\u793A\u601D\u8003\u6761\uFF08\u6B63\u5728\u601D\u8003 / \u6DF1\u5EA6\u601D\u8003\u4E2D\uFF09",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u663E\u793A\u601D\u8003\u6761", checked: thinkBar, onChange: setThinkBar })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u663E\u793A\u5FEB\u901F\u6309\u94AE\uFF08\u5207\u6362\u601D\u8003\u6863\u4F4D\uFF09",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u663E\u793A\u5FEB\u901F\u6309\u94AE", checked: quickButtons, onChange: setQuickButtons })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u601D\u8003\u6863\u4F4D\u6570\uFF081~5\uFF09",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("span", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-2)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { onClick: () => setThinkTiers(Math.max(1, thinkTiers - 1)), children: "\u2212" }),
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", children: thinkTiers }),
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { onClick: () => setThinkTiers(Math.min(5, thinkTiers + 1)), children: "\uFF0B" })
          ] })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "\u6BCF\u6863\u6807\u7B7E", value: thinkLabels, onChange: setThinkLabels, placeholder: "\u9017\u53F7\u5206\u9694\uFF0C\u5982 \u8F7B\u5EA6,\u6807\u51C6,\u6DF1\u5EA6", width: 340 }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "\u63A8\u7406\u5F3A\u5EA6\u53C2\u6570\u952E\u540D", value: thinkKey, onChange: setThinkKey, placeholder: "\u5982 reasoning_effort", width: 260 }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "\u6BCF\u6863\u53C2\u6570\u503C", value: thinkValues, onChange: setThinkValues, placeholder: "\u9017\u53F7\u5206\u9694\uFF0C\u5982 low,medium,high", width: 300 }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u63D0\u793A\uFF1A\u53C2\u6570\u952E\u540D\u4E0E\u6BCF\u6863\u503C\u9700\u4E0E\u4F60\u6240\u9009\u6A21\u578B\u7684\u63A8\u7406\u5F3A\u5EA6\u63A5\u53E3\u5BF9\u5E94\uFF08\u4F8B\u5982 OpenAI \u63A8\u7406\u6A21\u578B\u7528 reasoning_effort=low/medium/high\uFF09\u3002\u4E0D\u5339\u914D\u65F6\u8BE5\u6A21\u578B\u4F1A\u5FFD\u7565\u6216\u62A5\u9519\uFF0C\u5C5E\u6B63\u5E38\u914D\u7F6E\u5DEE\u5F02\u3002" })
    ] })
  ] });
}
function MemorySection() {
  const [turns, setTurns] = useStr("5");
  const [contextLimit, setContextLimit] = useStr("32K");
  const [windowLabel, setWindowLabel] = useStr("3 \u5929");
  const [randomness, setRandomness] = useNum(0.3);
  const [subProfile, setSubProfile] = useBool(false);
  const [semantic, setSemantic] = useBool(false);
  const [crossSave, setCrossSave] = useBool(false);
  const [coreMemory, setCoreMemory] = useBool(true);
  const [threshold, setThreshold] = useNum(0.82);
  const [trauma, setTrauma] = useBool(true);
  const [chainLimit, setChainLimit] = useNum(5);
  const MEMORY_WINDOWS = ["6 \u5C0F\u65F6", "12 \u5C0F\u65F6", "1 \u5929", "3 \u5929", "1 \u5468", "2 \u5468", "1 \u4E2A\u6708", "3 \u4E2A\u6708", "\u4E0D\u9650"];
  return /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(import_jsx_runtime46.Fragment, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u8BB0\u5FC6\u4E0E\u5BF9\u8BDD", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        PickRow,
        {
          title: "\u8BB0\u5FC6\u4E0A\u4E0B\u6587\u8F6E\u6570",
          desc: "AI \u6BCF\u6B21\u56DE\u590D\u53EF\u56DE\u5FC6\u8D77\u7684\u5386\u53F2\u6D88\u606F\u6761\u6570",
          value: turns,
          options: ["3", "5", "8", "10", "12", "15", "20"],
          onChange: setTurns,
          width: 110
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        PickRow,
        {
          title: "\u4E0A\u4E0B\u6587\u4E0A\u9650",
          value: contextLimit,
          options: ["4K", "8K", "16K", "32K", "64K", "128K", "256K"],
          onChange: setContextLimit,
          width: 180,
          label: "\u8D85\u8FC7\u540E\u5F3A\u5236\u603B\u7ED3"
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u5BF9\u8BDD\u5386\u53F2\u4F30\u7B97\u8D85\u8FC7\u8BE5 Token \u6570\u65F6\uFF0C\u4F1A\u50CF\u4EBA\u56DE\u5FC6\u4E00\u6837\u628A\u65E7\u5BF9\u8BDD\u603B\u7ED3\u6210\u7B2C\u4E00\u4EBA\u79F0\u8BB0\u5FC6\uFF1B\u5E73\u65F6\u4E0D\u52A8\u5386\u53F2\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        PickRow,
        {
          title: "\u9057\u5FD8\u6307\u6570",
          desc: "\u503C\u8D8A\u4F4E\u5FD8\u5F97\u8D8A\u5FEB\u3002\u9ED8\u8BA4\u53EA\u4EE5\u4E09\u5929\u5185\u7684\u4E8B\u4E3A\u4E3B\uFF0C\u8D8A\u4E45\u8D8A\u6A21\u7CCA\uFF0C\u4F46\u8BB0\u5F97\u5F88\u7262\u7684\u4E8B\u4E0D\u4F1A\u5F52\u96F6\uFF1B\u9009\u300C\u4E0D\u9650\u300D\u80FD\u60F3\u8D77\u6700\u65E9\u7684\u8BB0\u5FC6\u3002",
          value: windowLabel,
          options: MEMORY_WINDOWS,
          onChange: setWindowLabel,
          width: 110,
          label: "\u9057\u5FD8\u5FEB\u6162"
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u56DE\u5FC6\u7684\u968F\u673A\u5EA6",
          desc: "\u8D8A\u5927\u8D8A\u50CF\u4EBA\uFF1A\u4F1A\u8DF3\u7740\u60F3\u3001\u5076\u5C14\u60F3\u4E0D\u8D77\u6765\u30020 = \u4E25\u683C\u6309\u65F6\u95F4\u4E0E\u91CD\u8981\u6027\u6392\u5E8F\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(GlassSlider, { label: "\u56DE\u5FC6\u7684\u968F\u673A\u5EA6", width: 140, value: randomness, onChange: setRandomness })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u9700\u8981\u4E2A\u522B\u89D2\u8272\u8BB0\u6027\u597D/\u8BB0\u6027\u5DEE\u65F6\uFF0C\u5728\u89D2\u8272\u5E93 \u2192\u300C\u53C2\u6570\u8986\u5199\u300D\u91CC\u5355\u72EC\u8BBE\u7F6E\u8FD9\u4E24\u4E2A\u503C\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u8BA9\u6A21\u578B\u53C2\u4E0E\u526F\u8BBE\u5B9A\u6539\u5199",
          desc: "\u5F00\u542F\u540E\u6BCF 12 \u8F6E\u5BF9\u8BDD\uFF0C\u8BA9\u6A21\u578B\u91CD\u65B0\u8868\u8FF0\u4E00\u6B21\u300CTA \u73B0\u5728\u7684\u6837\u5B50\u300D\uFF08\u6027\u683C\u4E0E\u5904\u5883\uFF09\uFF1B\u4E3B\u8BBE\u5B9A\u4E0D\u52A8\u3001\u968F\u65F6\u53EF\u56DE\u6B63\u3002\u9ED8\u8BA4\u5173\u95ED\u4EE5\u514D\u989D\u5916\u82B1\u94B1\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u8BA9\u6A21\u578B\u53C2\u4E0E\u526F\u8BBE\u5B9A\u6539\u5199", checked: subProfile, onChange: setSubProfile })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u8BA9\u6A21\u578B\u8F85\u52A9\u5EFA\u7ACB\u5173\u7CFB\u7F51",
          desc: "\u5173\u8054\u7F51\u9ED8\u8BA4\u53EA\u9760\u5171\u73B0\u7EDF\u8BA1\u81EA\u52A8\u957F\uFF08\u4E0D\u82B1\u94B1\uFF09\uFF1B\u6253\u5F00\u540E\u6BCF 20 \u8F6E\u8BA9\u6A21\u578B\u8865 3 \u6761\u5173\u8054\uFF0C\u6807\u4E3A AI \u6765\u6E90\uFF0C\u4EBA\u5DE5\u7EA0\u9519\u4ECD\u4F18\u5148\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u8BA9\u6A21\u578B\u8F85\u52A9\u5EFA\u7ACB\u5173\u7CFB\u7F51", checked: semantic, onChange: setSemantic })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u8DE8\u5B58\u6863\u5171\u7528\u8BB0\u5FC6",
          desc: "\u9ED8\u8BA4\u5173\u95ED\uFF1A\u6BCF\u4E2A\u5B58\u6863\u5404\u8BB0\u5404\u7684\uFF0C\u6362\u4E00\u4E2A\u6863\uFF0CTA \u4E0D\u8BB0\u5F97\u522B\u5904\u53D1\u751F\u8FC7\u4EC0\u4E48\u3002\u6253\u5F00\u540E\u6240\u6709\u5B58\u6863\u8BFB\u5199\u540C\u4E00\u4EFD\u8BB0\u5FC6\u4E0E\u5173\u7CFB\u7F51\uFF1B\u60F3\u8BA9\u4E24\u4E2A\u4EBA\u5206\u5F00\uFF0C\u5C31\u53BB\u89D2\u8272\u5E93\u300C\u590D\u5236\u89D2\u8272 \xB7 \u8FDE\u8BB0\u5FC6\u4E00\u8D77\u590D\u5236\u300D\u5206\u4E00\u4EFD\u51FA\u6765\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u8DE8\u5B58\u6863\u5171\u7528\u8BB0\u5FC6", checked: crossSave, onChange: setCrossSave })
        }
      )
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u8BB0\u5FC6\u7684\u6DF1\u5904", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u6838\u5FC3\u8BB0\u5FC6",
          desc: "\u60C5\u7EEA\u5F3A\u5EA6\u4E0E\u91CD\u8981\u6027\u90FD\u8FC7\u7EBF\u7684\u4E8B\u4F1A\u6210\u4E3A\u6838\u5FC3\u8BB0\u5FC6\uFF1A\u6C38\u4E0D\u9057\u5FD8\u3001\u5B8C\u6574\u8C41\u514D\u91CD\u590D\u673A\u5236\u3001\u59CB\u7EC8\u53EF\u56DE\u60F3\uFF0C\u5E76\u50CF\u951A\u4E00\u6837\u5F71\u54CD\u5468\u56F4\u7684\u8BB0\u5FC6\u3002\u9608\u503C\u8D8A\u9AD8\u8D8A\u96BE\u6210\u4E3A\u6838\u5FC3\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u6838\u5FC3\u8BB0\u5FC6", checked: coreMemory, onChange: setCoreMemory })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u6210\u4E3A\u6838\u5FC3\u7684\u9608\u503C",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            RangeSlider,
            {
              label: "\u6210\u4E3A\u6838\u5FC3\u7684\u9608\u503C",
              min: 0.5,
              max: 0.99,
              step: 0.01,
              value: threshold,
              onChange: setThreshold,
              width: 140,
              disabled: !coreMemory
            }
          )
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("p", { className: "cap", children: [
        "\u5F53\u524D\u9608\u503C\uFF1A",
        threshold.toFixed(2)
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u521B\u4F24\u53EF\u4EE5\u88AB\u6CBB\u6108",
          desc: "\u521B\u4F24\u4E0D\u4F1A\u88AB\u5220\u9664\uFF0C\u53EA\u80FD\u88AB\u6CBB\u6108\uFF1A\u9760\u5177\u4F53\u7684\u5B89\u6170\u4E0E\u966A\u4F34\u6162\u6162\u964D\u4F4E\u60C5\u7EEA\u5F3A\u5EA6\uFF0C\u65F6\u95F4\u51E0\u4E4E\u4E0D\u8D77\u4F5C\u7528\u3002\u4F1A\u4ECE\u4E00\u78B0\u5C31\u75DB\uFF0C\u8D70\u5230\u80FD\u5E73\u9759\u5730\u53F9\u53E3\u6C14\uFF0C\u6700\u540E\u878D\u8FDB\u6027\u683C\u4E0E\u884C\u4E3A\u91CC\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u521B\u4F24\u53EF\u4EE5\u88AB\u6CBB\u6108", checked: trauma, onChange: setTrauma })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u94FE\u5F0F\u641C\u7D22\u6761\u6570",
          desc: "\u4ECE\u4E00\u6761\u7EBF\u7D22\u987A\u7740\u5171\u7528\u8BCD\u3001\u90BB\u8FD1\u65F6\u95F4\u3001\u540C\u4E00\u4E2A\u5B57\u4E0E\u76F8\u540C\u7279\u8D28\u5F80\u4E0B\u627E\u3002\u9ED8\u8BA4 5 \u6761\uFF1B\u540C\u65F6\u95F4\u5185\u8D8A\u76F8\u4F3C\u8D8A\u9760\u524D\uFF0C\u65B0\u4E8B\u4F18\u5148\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            RangeSlider,
            {
              label: "\u94FE\u5F0F\u641C\u7D22\u6761\u6570",
              min: 1,
              max: 20,
              step: 1,
              value: chainLimit,
              onChange: setChainLimit,
              width: 140
            }
          )
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("p", { className: "cap", children: [
        "\u6BCF\u6B21\u6700\u591A\u627E ",
        chainLimit.toFixed(0),
        " \u6761"
      ] })
    ] })
  ] });
}
function PersonaSection() {
  const [narrator, setNarrator] = useBool(false);
  const [gender, setGender] = useStr("\u672A\u8BBE\u7F6E");
  const [birthday, setBirthday] = useStr("");
  const [newGenderName, setNewGenderName] = useStr("");
  const [newGenderPronoun, setNewGenderPronoun] = useStr("TA");
  const [newGenderDesc, setNewGenderDesc] = useStr("");
  const [scriptLang, setScriptLang] = useStr("js");
  const [script, setScript] = useStr("");
  const [mental, setMental] = useBool(true);
  const [psychBehavior, setPsychBehavior] = useBool(true);
  const [physiology, setPhysiology] = useBool(true);
  const [innerLife, setInnerLife] = useBool(true);
  const [concretization, setConcretization] = useBool(true);
  const [innerDrive, setInnerDrive] = useBool(true);
  const [toolCalls, setToolCalls] = useBool(false);
  const [chatFull, setChatFull] = useBool(false);
  const [statAi, setStatAi] = useBool(true);
  const [brain, setBrain] = useBool(true);
  const [expectation, setExpectation] = useBool(true);
  const [surprise, setSurprise] = useBool(true);
  const [brainAi, setBrainAi] = useBool(false);
  const [brainUrl, setBrainUrl] = useStr("");
  const [brainKey, setBrainKey] = useStr("");
  const [brainModel, setBrainModel] = useStr("");
  const [brainTurns, setBrainTurns] = useNum(8);
  return /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(import_jsx_runtime46.Fragment, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u8EAB\u4EFD\u4E0E\u65C1\u767D", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u65C1\u767D AI",
          desc: "\u5F00\u542F\u540E\uFF0C\u56DE\u590D\u91CC\u5141\u8BB8\u51FA\u73B0\u7B80\u77ED\u7684\u7B2C\u4E09\u4EBA\u79F0\u73AF\u5883/\u52A8\u4F5C\u63CF\u5199\uFF08\u300C\u5979\u628A\u676F\u5B50\u653E\u56DE\u684C\u4E0A\u300D\uFF09\u3002\u4E0D\u66FF\u4F60\u505A\u51B3\u5B9A\uFF0C\u4E5F\u4E0D\u63CF\u5199\u4F60\u7684\u5FC3\u7406\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u65C1\u767D AI", checked: narrator, onChange: setNarrator })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        PickRow,
        {
          title: "\u4F60\u7684\u6027\u522B",
          desc: "\u53EA\u5F71\u54CD\u79F0\u547C\u4E0E\u76F8\u5904\u65B9\u5F0F\uFF0C\u4E0D\u5199\u6B7B\u523B\u677F\u5370\u8C61\uFF1B\u7559\u7A7A = \u4E0D\u6307\u5B9A\u3002\u89D2\u8272\u81EA\u5DF1\u7684\u6027\u522B\u5728\u89D2\u8272\u5E93\u91CC\u8BBE\u3002",
          value: gender,
          options: ["\u672A\u8BBE\u7F6E", "\u5973", "\u7537", "\u5176\u4ED6"],
          onChange: setGender,
          width: 140
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u4F60\u7684\u751F\u65E5",
          desc: "\u7559\u7A7A = \u4E0D\u8BBE\u7F6E\u3002\u586B\u4E86\u4E4B\u540E AI \u4F1A\u8BB0\u5F97\u8FD9\u4E00\u5929\uFF1B\u53EA\u5B58\u6708\u65E5\uFF0C\u4E0D\u9700\u8981\u5E74\u4EFD\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { style: { display: "inline-block", width: 140 }, children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Field, { value: birthday, onChange: setBirthday, placeholder: "MM-dd\uFF0C\u5982 08-02", label: "\u4F60\u7684\u751F\u65E5" }) })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(GroupLabel, { children: "\u81EA\u5B9A\u4E49\u6027\u522B" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "s-row__desc", children: "\u540D\u5B57 + \u79F0\u547C + \u4E00\u6BB5\u8BF4\u660E\u5373\u53EF\uFF1B\u8FD8\u53EF\u4EE5\u6302\u4E00\u6BB5\u811A\u672C\uFF08JS/Python/Java\uFF09\uFF0C\u8BA9\u6027\u522B\u51B3\u5B9A\u5468\u671F\u3001\u79F0\u547C\u6216\u968F\u7ECF\u5386\u53D8\u5316\u7684\u63CF\u8FF0\u3002\u811A\u672C\u8F93\u5165 JSON\u3001\u8F93\u51FA\u6587\u5B57\uFF0C\u8D85\u65F6\u4E0E\u8BED\u53E5\u6570\u90FD\u6709\u786C\u4E0A\u9650\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 110px", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Field, { value: newGenderName, onChange: setNewGenderName, placeholder: "\u540D\u5B57\uFF08\u5982\uFF1A\u6276\u5979 / \u65E0\u6027\u522B / \u6D41\u6027\uFF09", label: "\u540D\u5B57" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Field, { value: newGenderPronoun, onChange: setNewGenderPronoun, placeholder: "\u81EA\u79F0\uFF08TA\uFF09", label: "\u81EA\u79F0" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Field, { value: newGenderDesc, onChange: setNewGenderDesc, placeholder: "\u4E00\u53E5\u8BDD\u8BF4\u660E\uFF08\u4F1A\u8FDB\u63D0\u793A\u8BCD\uFF09", label: "\u8BF4\u660E" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", children: "\u811A\u672C\u8BED\u8A00" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Select, { value: scriptLang, options: opts(["js", "python", "java"]), onChange: setScriptLang, label: "\u811A\u672C\u8BED\u8A00" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        Field,
        {
          value: script,
          onChange: setScript,
          placeholder: '\u53EF\u9009\uFF1A\u811A\u672C\u4EE3\u7801\uFF08\u8F93\u5165 JSON\uFF1A{"name":"..."}\uFF0C\u8FD4\u56DE\u4E00\u6BB5\u6587\u5B57\uFF09',
          multiline: true,
          label: "\u811A\u672C\u4EE3\u7801"
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr auto", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(SignalButton, { children: "\u4FDD\u5B58\u81EA\u5B9A\u4E49\u6027\u522B" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u4ECE JSON \u5BFC\u5165" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr auto", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
          Select,
          {
            value: "\u8FD8\u6CA1\u6709\u81EA\u5B9A\u4E49\u6027\u522B",
            options: [{ value: "\u8FD8\u6CA1\u6709\u81EA\u5B9A\u4E49\u6027\u522B", label: "\u8FD8\u6CA1\u6709\u81EA\u5B9A\u4E49\u6027\u522B" }],
            onChange: () => void 0,
            label: "\u9009\u62E9\u8981\u5220\u9664\u7684\u81EA\u5B9A\u4E49\u6027\u522B"
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u5220\u9664" })
      ] })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u5FC3\u7406\u4E0E\u8EAB\u4F53", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u5FC3\u7406\u72B6\u51B5\u7CFB\u7EDF",
          desc: "\u53EF\u4E3A\u89D2\u8272\u8BBE\u7F6E\u8F7B/\u4E2D/\u91CD\u5EA6\u5FC3\u7406\u72B6\u51B5\uFF08\u5185\u7F6E 115 \u6761\uFF1A\u60C5\u7EEA\u3001\u7126\u8651\u3001\u521B\u4F24\u3001\u5F3A\u8FEB\u3001\u8FDB\u98DF\u3001\u7761\u7720\u3001\u7CBE\u795E\u75C5\u6027\u3001\u4EBA\u683C\u3001\u795E\u7ECF\u53D1\u80B2\u3001\u795E\u7ECF\u3001\u8EAF\u4F53\u3001\u51B2\u52A8\u4E0E\u7279\u8D28\uFF09\u3002\u53EA\u628A\u5F53\u524D\u89D2\u8272\u751F\u6548\u7684\u6761\u76EE\u5199\u8FDB\u63D0\u793A\u8BCD\uFF0C\u4E0D\u8D34\u6807\u7B7E\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u5FC3\u7406\u72B6\u51B5\u7CFB\u7EDF", checked: mental, onChange: setMental })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(
        "div",
        {
          style: {
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            gap: "var(--w-space-2)",
            padding: "4px 0"
          },
          children: [
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__desc", children: "\u6253\u5F00\u540E\u53EF\u9010\u89D2\u8272\u8BBE\u5B9A\uFF1B\u6D89\u53CA\u81EA\u4F24/\u81EA\u6740/\u4F24\u4EBA\u7684\u6761\u76EE\u6709\u786C\u8FB9\u754C\uFF1A\u4E0D\u63D0\u4F9B\u65B9\u6CD5\u3001\u4E0D\u7F8E\u5316\uFF0C\u53EA\u505A\u966A\u4F34\u4E0E\u8F6C\u5411\u6C42\u52A9\u3002" }),
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u7BA1\u7406\u5FC3\u7406\u72B6\u51B5" })
          ]
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u5FC3\u7406\u9A71\u52A8\u7684\u884C\u4E3A",
          desc: "\u5141\u8BB8\u89D2\u8272\u56E0\u5FC3\u7406\u72B6\u6001\u505A\u51FA\u56DE\u907F\u3001\u4F9D\u8D56\u3001\u6613\u6012\u7B49\u53CD\u5E94\u3002\u5B89\u5168\u6761\u76EE\u6C38\u8FDC\u4F18\u5148\uFF1A\u4E0D\u4F1A\u751F\u6210\u81EA\u4F24/\u4F24\u4EBA\u7684\u5177\u4F53\u505A\u6CD5\uFF0C\u4E5F\u4E0D\u4F1A\u9F13\u52B1\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u5FC3\u7406\u9A71\u52A8\u7684\u884C\u4E3A", checked: psychBehavior, onChange: setPsychBehavior })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u8EAB\u4F53\u611F\u53D7\uFF08\u51B7\u6696 / \u9965\u6E34 / \u60F3\u5FF5\uFF09",
          desc: "\u6309\u5929\u6C14\u4E0E\u65F6\u95F4\u7F13\u6162\u6F02\u79FB\uFF0C\u804A\u5929\u4E0E\u4EB2\u5BC6\u4E92\u52A8\u4F1A\u6539\u53D8\u5B83\uFF1B\u89D2\u8272\u4F1A\u8BF4\u300C\u6709\u70B9\u51B7\u300D\u300C\u60F3\u4F60\u4E86\u300D\uFF0C\u800C\u4E0D\u662F\u62A5\u6570\u503C\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u8EAB\u4F53\u611F\u53D7", checked: physiology, onChange: setPhysiology })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u5185\u5FC3\u751F\u6D3B\uFF08\u5185\u5206\u6CCC / \u6267\u5FF5\uFF09",
          desc: "\u5979\u7684\u5FC3\u7EEA\u6709\u81EA\u5DF1\u7684\u6765\u5904\uFF1A\u6E38\u620F\u91CC\u88AB\u6253\u3001\u88AB\u5582\u98DF\uFF0C\u804A\u5929\u91CC\u7684\u51B7\u6696\u8BDD\uFF0C\u90FD\u4F1A\u8BA9\u6FC0\u7D20\u6DA8\u843D\u5E76\u6E17\u8FDB\u8BED\u6C14\uFF1B\u5979\u8BF4\u300C\u4E0D\u77E5\u9053\u300D\u7684\u4E8B\u4F1A\u957F\u6210\u6267\u5FF5\uFF0C\u60E6\u8BB0\u5230\u4E00\u5B9A\u7A0B\u5EA6\u4F1A\u81EA\u5DF1\u60F3\u660E\u767D\u3001\u4E3B\u52A8\u6765\u8BB2\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u5185\u5FC3\u751F\u6D3B", checked: innerLife, onChange: setInnerLife })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u60C5\u7EEA\u5177\u4F53\u5316\uFF08\u8EAB\u4F53 / \u884C\u4E3A\uFF09",
          desc: "\u60C5\u7EEA\u4F1A\u843D\u5230\u8EAB\u4F53\u4E0E\u884C\u4E3A\u4E0A\uFF1A\u5F71\u54CD\u98DF\u6B32\u3001\u60F3\u4E0D\u60F3\u8BF4\u8BDD\u3001\u60F3\u4E0D\u60F3\u8D70\u52A8\uFF1B\u957F\u671F\u538B\u6291\u6216\u9AD8\u5524\u8D77\u8FD8\u53EF\u80FD\u8EAF\u4F53\u5316\u2014\u2014\u809A\u5B50\u75BC\u3001\u80C3\u707C\u70E7\u3001\u547C\u5438\u6025\u4FC3\u3002\u5224\u5B9A\u5168\u90E8\u8D70\u7EAF\u4EE3\u7801\uFF0C\u53EA\u5728\u5BF9\u8BDD\u91CC\u81EA\u7136\u6E17\u8FDB\u8BED\u6C14\u4E0E\u52A8\u4F5C\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u60C5\u7EEA\u5177\u4F53\u5316", checked: concretization, onChange: setConcretization })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u5185\u9A71\u52A8\u7CFB\u7EDF\uFF08\u57FA\u672C\u52A8\u529B / \u4E03\u4E2A\u9A71\u52A8 / \u81EA\u5F8B\uFF09",
          desc: "\u7ED9\u5979\u4E00\u5957\u81EA\u5DF1\u7684\u52A8\u673A\uFF1A\u653B\u51FB\u6027\u3001\u6027\u3001\u81EA\u604B\u4E09\u79CD\u57FA\u672C\u52A8\u529B\u51B3\u5B9A\u5979\u591A\u5BB9\u6613\u88AB\u63A8\u52A8\uFF1B\u8D2A\u6B32\u3001\u66B4\u98DF\u3001\u8272\u6B32\u3001\u5AC9\u5992\u3001\u50B2\u6162\u3001\u6020\u60F0\u3001\u66B4\u6012\u4E03\u4E2A\u9A71\u52A8\u51B3\u5B9A\u5979\u6B64\u523B\u60F3\u505A\u4EC0\u4E48\uFF0C\u505A\u6210\u4E4B\u540E\u5316\u6210\u5E72\u52B2\u4E0E\u677E\u5FEB\u3002\u81EA\u5F8B\u662F\u95F8\u95E8\uFF0C\u65B9\u5411\u7531\u89D2\u8272\u7684\u9053\u5FB7\u89C2\u3001\u4EBA\u751F\u89C2\u3001\u601D\u7EF4\u4E3B\u4E49\u51B3\u5B9A\uFF0C\u4E5F\u4F1A\u88AB\u5979\u81EA\u5DF1\u7684\u514B\u5236\u4E0E\u653E\u7EB5\u7EC3\u786C\u6216\u78E8\u8F6F\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u5185\u9A71\u52A8\u7CFB\u7EDF", checked: innerDrive, onChange: setInnerDrive })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "s-row__desc", children: "\u751F\u6C14\u5728\u8FD9\u4E00\u5C42\u88AB\u62C6\u6210\u516B\u79CD\u51FA\u53E3\uFF0C\u4ECE\u5F3A\u5230\u5F31\uFF1A\u53DB\u9006\u3001\u6492\u6CFC\u3001\u6004\u6C14\u3001\u4EFB\u6027\u3001\u90C1\u95F7\u3001\u62D6\u5EF6\u3001\u538C\u70E6\u3001\u9000\u7F29\u3002\u8D70\u54EA\u4E2A\u51FA\u53E3\u7531\u706B\u6C14\u3001\u653B\u51FB\u6027\u4E0E\u81EA\u5F8B\u5171\u540C\u51B3\u5B9A\uFF0C\u8BF4\u5B8C\u4F1A\u677E\u4E00\u53E3\uFF0C\u5FCD\u7740\u5219\u7559\u4E0B\u95F7\u5E76\u628A\u81EA\u5F8B\u7EC3\u786C\u4E00\u70B9\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u663E\u793A\u5DE5\u5177\u8C03\u7528",
          desc: "\u52FE\u9009\u540E\uFF0C\u5BF9\u8BDD\u91CC\u4F1A\u51FA\u73B0\u4E00\u6761\u53EF\u5C55\u5F00\u7684\u300C\u5DE5\u5177\u8C03\u7528\u300D\u6C14\u6CE1\uFF0C\u80FD\u770B\u5230\u6BCF\u6B21\u8C03\u7528\u7684\u540D\u5B57\u3001\u53C2\u6570\u548C\u8FD4\u56DE\u7ED3\u679C\uFF1B\u4E0D\u52FE\u5C31\u5B8C\u5168\u4E0D\u663E\u793A\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u663E\u793A\u5DE5\u5177\u8C03\u7528", checked: toolCalls, onChange: setToolCalls })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u5BF9\u8BDD\u4FE1\u606F\u5168\u5C4F",
          desc: "\u628A\u804A\u5929\u5185\u5BB9\u94FA\u6EE1\u6574\u5C4F\uFF0C\u9690\u85CF\u8054\u7CFB\u4EBA\u680F\uFF1B\u804A\u5929\u9875\u9876\u90E8\u6709\u300C\u8FD4\u56DE\u300D\u6309\u94AE\u968F\u65F6\u9000\u56DE\u3002\u5207\u9875\u524D\u4F1A\u8BB0\u4F4F\u8FD9\u6B21\u7684\u9009\u62E9\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u5BF9\u8BDD\u4FE1\u606F\u5168\u5C4F", checked: chatFull, onChange: setChatFull })
        }
      )
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u6570\u503C\u7ED3\u7B97", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u6570\u503C\u7531 AI \u63A7\u5236",
          desc: "\u597D\u611F\u3001\u4FE1\u4EFB\u3001\u7CBE\u529B\u7684\u589E\u51CF\u4EA4\u7ED9 AI \u5224\uFF08\u56DE\u590D\u91CC\u5185\u8054\u6307\u4EE4\u4E3A\u4E3B\uFF0C\u6CA1\u7ED9\u5C31\u515C\u5E95\u95EE\u4E00\u6B21\uFF0C\u5224\u4E0D\u52A8\u5C31\u4E0D\u52A8\uFF09\u3002\u5173\u6389\u5219\u56DE\u5230\u65E7\u7684\u56FA\u5B9A\u589E\u91CF\uFF1A\u8BF4\u4E00\u53E5 +1\u3001\u52A8\u4F5C +2/+3/+5\u3001\u7CBE\u529B\u7EAF\u6309\u6E05\u9192\u65F6\u957F\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u6570\u503C\u7531 AI \u63A7\u5236", checked: statAi, onChange: setStatAi })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(GroupLabel, { children: "\u5927\u8111\u9884\u5224" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u5F00\u542F\u5927\u8111\u9884\u5224",
          desc: "TA \u4F1A\u4E0B\u610F\u8BC6\u9884\u4F30\u4F60\u8FD9\u6B21\u4F1A\u6709\u591A\u70ED\uFF1A\u8D85\u9884\u671F\u5C31\u53D7\u7528\u3001\u597D\u611F\u6DA8\u5F97\u5FEB\u4E9B\uFF0C\u4F4E\u4E8E\u9884\u671F\u5C31\u628A\u70ED\u60C5\u6536\u7740\u3002\u5173\u6389\u540E\u4E00\u5207\u7167\u65E7\uFF08\u597D\u611F\u6052\u5B9A +1\uFF09\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u5F00\u542F\u5927\u8111\u9884\u5224", checked: brain, onChange: setBrain })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u671F\u5F85\u5DEE\uFF08\u591A\u5DF4\u80FA\uFF09",
          desc: "\u6309\u9884\u6D4B\u8BEF\u5DEE\u6D6E\u52A8\u597D\u611F\u6DA8\u901F\uFF08\u7EA6 0.2~1.8 \u500D\uFF09\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u671F\u5F85\u5DEE", checked: expectation, onChange: setExpectation, disabled: !brain })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u610F\u6599\u4E4B\u5916\uFF08\u60CA\u559C\uFF09",
          desc: "\u5076\u5C14\u4E0D\u5B8C\u5168\u987A\u7740\u6700\u987A\u7684\u8D70\u5411\uFF1A\u505C\u534A\u62CD\u3001\u6362\u4E2A\u89D2\u5EA6\u3001\u63D0\u4E00\u4EF6\u597D\u5947\u7684\u5C0F\u4E8B\u3002\u53EA\u5FAE\u5FAE\u5236\u9020\u4E00\u70B9\u9884\u6D4B\u4E0D\u5230\u7684\u5FC3\u52A8\uFF0C\u60C5\u7EEA\u4F4E\u843D\u65F6\u81EA\u52A8\u505C\u7528\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u610F\u6599\u4E4B\u5916", checked: surprise, onChange: setSurprise, disabled: !brain })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(GroupLabel, { children: "\u514D\u8D39\u5C0F\u6A21\u578B\u8F85\u52A9\u9884\u5224\uFF08\u53EF\u9009\uFF09" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "s-row__desc", children: "\u9ED8\u8BA4\u5B8C\u5168\u672C\u5730\u8DD1\u3001\u4E0D\u82B1\u94B1\u3002\u60F3\u66F4\u51C6\u53EF\u6307\u5B9A\u4E00\u679A\u4FBF\u5B9C/\u514D\u8D39\u7684\u6A21\u578B\uFF0C\u6BCF N \u8F6E\u8865\u4E00\u6B21\u300C\u9884\u5224\u4E0B\u4E00\u6761\u591A\u70ED\u300D\uFF0C\u7ED3\u679C\u7F13\u5B58\u8FDB\u5B58\u6863\uFF1B\u4E0D\u5F00\u5C31\u7528\u672C\u5730\u542F\u53D1\u5F0F\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u7528\u5C0F\u6A21\u578B\u8865\u9884\u5224",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u7528\u5C0F\u6A21\u578B\u8865\u9884\u5224", checked: brainAi, onChange: setBrainAi, disabled: !brain })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "\u63A5\u53E3\u5730\u5740", value: brainUrl, onChange: setBrainUrl, placeholder: "\u7559\u7A7A\u5219\u6CBF\u7528\u4E0A\u9762\u7684 AI \u5730\u5740", width: 340 }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        FieldRow,
        {
          title: "\u5BC6\u94A5",
          value: brainKey,
          onChange: setBrainKey,
          placeholder: "\u7559\u7A7A\u5219\u6CBF\u7528\u4E0A\u9762\u7684 AI \u5BC6\u94A5\uFF08\u53EA\u5B58\u672C\u5730\uFF0C\u4E0D\u8FDB\u4E91\u5B58\u6863\uFF09",
          type: "password",
          width: 340
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "\u6A21\u578B", value: brainModel, onChange: setBrainModel, placeholder: "\u4E00\u4E2A\u4FBF\u5B9C/\u514D\u8D39\u7684\u6A21\u578B\u540D", width: 300 }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: `\u6BCF ${brainTurns} \u8F6E\u8865\u4E00\u6B21`,
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            RangeSlider,
            {
              label: "\u6BCF N \u8F6E\u8865\u4E00\u6B21",
              min: 2,
              max: 30,
              step: 1,
              value: brainTurns,
              onChange: setBrainTurns,
              width: 140,
              disabled: !brainAi
            }
          )
        }
      )
    ] })
  ] });
}
function InterfaceSection() {
  const [palette] = useStr("\u7EC8\u672B\u5730");
  const [menuRight, setMenuRight] = useBool(false);
  const [lang, setLang] = useStr("\u7B80\u4F53\u4E2D\u6587");
  const [autoSave, setAutoSave] = useBool(true);
  const [chatStyle, setChatStyle] = useStr("\u5267\u672C\u5F0F");
  const [allowSilence, setAllowSilence] = useBool(false);
  const [orientation, setOrientation] = useStr("\u81EA\u52A8\uFF08\u7AD6\u5C4F\uFF09");
  const [tileOnline, setTileOnline] = useBool(true);
  const [difficulty, setDifficulty] = useStr("\u666E\u901A\uFF08\u9ED8\u8BA4\uFF09");
  const [autoDifficulty, setAutoDifficulty] = useBool(false);
  const [chess, setChess] = useBool(false);
  const [chessUrl, setChessUrl] = useStr("");
  const [chessKey, setChessKey] = useStr("");
  const [chessModel, setChessModel] = useStr("gpt-4o-mini");
  const [money, setMoney] = useBool(true);
  const [moneyInput, setMoneyInput] = useStr("100");
  const [widgets, setWidgets] = useBool(false);
  const [graphSkip, setGraphSkip] = useBool(false);
  const [skipNodes, setSkipNodes] = useNum(240);
  const [saveOobe, setSaveOobe] = useBool(true);
  const [outfitNew, setOutfitNew] = useBool(true);
  const [maskNames, setMaskNames] = useBool(false);
  const [libido, setLibido] = useBool(true);
  const [libidoCycle, setLibidoCycle] = useNum(0.5);
  return /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(import_jsx_runtime46.Fragment, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u754C\u9762", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u8BED\u4E49\u8C03\u8272\u677F",
          desc: "\u8986\u76D6\u6309\u94AE\u3001\u6587\u5B57\u3001\u9762\u677F\u3001\u80CC\u666F\u7684\u5173\u952E\u8272\uFF08\u53EA\u4FDD\u7559\u7EC8\u672B\u5730\uFF1B\u5730\u9762\u8272\u5728\u300C\u7EAF\u8272\u5E95\u300D\u91CC\u9009\uFF09",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Select, { value: palette, options: [{ value: palette, label: palette }], onChange: () => void 0, label: "\u8BED\u4E49\u8C03\u8272\u677F" })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u4E3B\u9898\u5C45\u5DE6\uFF0C\u6309\u94AE\u5C45\u53F3",
          desc: "\u7535\u8111\u7AEF\u4E3B\u83DC\u5355\u6309\u94AE\u4E0E\u4E3B\u9898\u88C5\u9970\u5217\u4E92\u6362\u4F4D\u7F6E\uFF08\u9ED8\u8BA4\u6309\u94AE\u5C45\u5DE6\uFF09",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u4E3B\u9898\u5C45\u5DE6\uFF0C\u6309\u94AE\u5C45\u53F3", checked: menuRight, onChange: setMenuRight })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        PickRow,
        {
          title: "\u8BED\u8A00",
          desc: "\u754C\u9762\u663E\u793A\u8BED\u8A00",
          value: lang,
          options: ["\u7B80\u4F53\u4E2D\u6587", "\u7E41\u9AD4\u4E2D\u6587", "English", "\u65E5\u672C\u8A9E"],
          onChange: setLang,
          width: 120
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u81EA\u52A8\u4FDD\u5B58",
          desc: "\u5F00\u542F\u540E\u8BBE\u7F6E\u53D8\u66F4\u5373\u65F6\u4FDD\u5B58",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u81EA\u52A8\u4FDD\u5B58", checked: autoSave, onChange: setAutoSave })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        PickRow,
        {
          title: "\u591A\u89D2\u8272\u804A\u5929\u663E\u793A",
          desc: "\u6C14\u6CE1\u5F0F\uFF1A\u6D88\u606F\u76F4\u63A5\u6210\u5BF9\u8BDD\u6C14\u6CE1\u6D41\uFF08\u9002\u5408\u591A\u89D2\u8272\u540C\u573A\uFF09\uFF1B\u5267\u672C\u5F0F\uFF1A\u5E95\u90E8\u5BF9\u8BDD\u9762\u677F + \u7ACB\u7ED8",
          value: chatStyle,
          options: ["\u5267\u672C\u5F0F", "\u6C14\u6CE1\u5F0F"],
          onChange: setChatStyle,
          width: 110
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u5141\u8BB8 AI \u6C89\u9ED8",
          desc: "\u5F00\u542F\u540E\u89D2\u8272\u53EF\u4EE5\u9009\u62E9\u4E0D\u56DE\u590D\uFF08\u4EC0\u4E48\u90FD\u4E0D\u8F93\u51FA\uFF09\uFF0C\u7CFB\u7EDF\u4F1A\u81EA\u52A8\u505C\u6B62\u8BE5\u56DE\u5408\uFF0C\u800C\u4E0D\u662F\u6BCF\u6B21\u63D0\u95EE\u90FD\u5F3A\u884C\u7ED9\u51FA\u7B54\u590D",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u5141\u8BB8 AI \u6C89\u9ED8", checked: allowSilence, onChange: setAllowSilence })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        PickRow,
        {
          title: "\u5C4F\u5E55\u65B9\u5411",
          desc: "\u79FB\u52A8\u7AEF\u4E13\u7528\uFF1A\u81EA\u52A8=\u9ED8\u8BA4\u7AD6\u5C4F\uFF0C\u624B\u673A/\u5E73\u677F\u90FD\u7AD6\u5C4F\uFF1B\u4E5F\u53EF\u624B\u52A8\u9501\u6A2A\u5C4F\uFF08\u7535\u8111\u7248\u5E03\u5C40\uFF09\u6216\u7AD6\u5C4F\u3002\u5207\u6362\u540E 3 \u79D2\u5185\u672A\u786E\u8BA4\u4F1A\u81EA\u52A8\u53D8\u56DE\u6765\u518D\u8BA9\u4F60\u786E\u8BA4\u4E00\u6B21",
          value: orientation,
          options: ["\u81EA\u52A8\uFF08\u7AD6\u5C4F\uFF09", "\u6A2A\u5C4F", "\u7AD6\u5C4F"],
          onChange: setOrientation,
          width: 130
        }
      )
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u5730\u56FE\u5E95\u56FE\u4E0E\u79BB\u7EBF\u5305", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "s-row__desc", children: "\u5730\u56FE\u9875\u7684\u5E95\u56FE\uFF1A\u9AD8\u5FB7 / OSM \u9700\u8981\u8054\u7F51\uFF1B\u79BB\u7EBF\u5305\u662F\u4E00\u4E2A .mbtiles \u6587\u4EF6\uFF0C\u6574\u4E2A\u5730\u533A\u6253\u5305\u5728\u672C\u5730\uFF0C\u6CA1\u7F51\u4E5F\u80FD\u770B\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u5141\u8BB8\u8054\u7F51\u53D6\u74E6\u7247",
          desc: "\u5173\u6389\u540E\u4E00\u4E2A\u8BF7\u6C42\u90FD\u4E0D\u53D1\uFF0C\u53EA\u8BFB\u672C\u5730\u7F13\u5B58\u4E0E\u79BB\u7EBF\u5305\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u5141\u8BB8\u8054\u7F51\u53D6\u74E6\u7247", checked: tileOnline, onChange: setTileOnline })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingBlock, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", children: "MBTiles \u79BB\u7EBF\u5305" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u8FD8\u6CA1\u6709\u9009\u62E9\u79BB\u7EBF\u5305\uFF0C\u5730\u56FE\u53EA\u7528\u8054\u7F51\u74E6\u7247\u4E0E\u7F13\u5B58\u3002" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("div", { children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(SignalButton, { children: "\u9009\u62E9 .mbtiles \u6587\u4EF6" }) }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u9009\u4E2D\u7684\u6587\u4EF6\u4F1A\u590D\u5236\u8FDB\u5E94\u7528\u76EE\u5F55\uFF0C\u539F\u6587\u4EF6\u53EF\u4EE5\u5220\u3002\u5305\u91CC\u6CA1\u6709\u7684\u6863\u4F4D\u6216\u533A\u57DF\u4F1A\u5728\u753B\u9762\u4E0A\u7528\u7236\u7EA7\u74E6\u7247\u9876\u7740\uFF0C\u4E0D\u4F1A\u4E00\u76F4\u7A7A\u767D\u3002" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(
        "div",
        {
          style: {
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            gap: "var(--w-space-3)",
            padding: "13px 0"
          },
          children: [
            /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { className: "s-row__text", children: [
              /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", children: "\u74E6\u7247\u7F13\u5B58" }),
              /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__desc", children: "\u8FD8\u6CA1\u6709\u7F13\u5B58\uFF0C\u8054\u7F51\u770B\u8FC7\u5730\u56FE\u4E4B\u540E\u8FD9\u91CC\u4F1A\u663E\u793A\u5360\u7528\u91CF\u3002" })
            ] }),
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u6E05\u7A7A\u74E6\u7247\u7F13\u5B58" })
          ]
        }
      )
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u5C0F\u6E38\u620F AI \u806A\u660E\u5EA6", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        PickRow,
        {
          title: "AI \u96BE\u5EA6",
          desc: "\u7B80\u5355=\u968F\u673A\u653E\u6C34\uFF0C\u666E\u901A=\u8D2A\u5403\uFF0C\u56F0\u96BE=\u524D\u77BB\u9632\u53CD",
          value: difficulty,
          options: ["\u7B80\u5355\uFF08\u65B0\u624B\u7EC3\u624B\uFF09", "\u666E\u901A\uFF08\u9ED8\u8BA4\uFF09", "\u56F0\u96BE\uFF08\u6B65\u6B65\u7D27\u903C\uFF09"],
          onChange: setDifficulty,
          width: 130,
          disabled: autoDifficulty
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "AI \u81EA\u52A8\u8C03\u8282",
          desc: "\u7531 AI \u6309\u4F60\u7684\u5BF9\u5C40\u8868\u73B0\u667A\u80FD\u5347\u964D\u96BE\u5EA6",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "AI \u81EA\u52A8\u8C03\u8282", checked: autoDifficulty, onChange: setAutoDifficulty })
        }
      )
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(
      SettingCard,
      {
        title: "\u4E91\u7AEF\u68CB\u529B\u8111\uFF08\u53EF\u9009\uFF09",
        note: "\u9ED8\u8BA4\u4F7F\u7528\u5185\u7F6E\u672C\u5730\u68CB\u529B\uFF0C\u4E0D\u4F9D\u8D56\u7F51\u7EDC\u3002\u586B\u5199 OpenAI \u517C\u5BB9\u4E0B\u68CB API \u540E\u53EF\u8BA9 AI \u66F4\u5F3A\u3002",
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            SettingRow,
            {
              title: "\u542F\u7528\u4E91\u7AEF\u68CB\u529B",
              control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u542F\u7528\u4E91\u7AEF\u68CB\u529B", checked: chess, onChange: setChess })
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "API \u5730\u5740", value: chessUrl, onChange: setChessUrl, placeholder: "https://api.openai.com/v1/chat/completions", width: 380 }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "API Key", value: chessKey, onChange: setChessKey, placeholder: "sk-\u2026", type: "password", width: 300 }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "\u6A21\u578B", value: chessModel, onChange: setChessModel, placeholder: "gpt-4o-mini", width: 240 })
        ]
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u91D1\u94B1\u7CFB\u7EDF", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u542F\u7528\u91D1\u94B1",
          desc: "\u5173\u95ED\u540E\u5546\u5E97\u4E0E\u793C\u7269\u4E0D\u518D\u6263\u94B1\uFF0C\u4F59\u989D\u539F\u6837\u4FDD\u7559\uFF1B\u518D\u6253\u5F00\u65F6\u7EE7\u7EED\u7528\u539F\u6765\u7684\u6570\u3002\u5F00\u7740\u65F6\u4E5F\u53EF\u4EE5\u5728\u8FD9\u91CC\u968F\u610F\u52A0\u94B1\u6216\u6539\u4F59\u989D\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u542F\u7528\u91D1\u94B1", checked: money, onChange: setMoney })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u4F59\u989D\u7531\u540E\u7AEF\u8BFB\u53D6\uFF0C\u8FD9\u91CC\u5148\u4E0D\u663E\u793A\u6570\u76EE\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr auto auto", gap: "var(--w-space-2)", padding: "10px 0" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Field, { value: moneyInput, onChange: setMoneyInput, placeholder: "\u91D1\u989D\uFF08\u53EF\u8D1F\u6570\uFF09", type: "number", label: "\u91D1\u989D" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(SignalButton, { children: "\u589E\u52A0" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u8BBE\u4E3A\u4F59\u989D" })
      ] })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u754C\u9762\u7EC4\u4EF6", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u4FE1\u606F\u680F\u6539\u7528\u5C0F\u7EC4\u4EF6",
          desc: "\u4E3B\u754C\u9762\u5DE6\u680F\u4E0E\u9876\u680F\u7531\u53EF\u6311\u9009\u7684\u5C0F\u7EC4\u4EF6\u7EC4\u6210\uFF1A\u65F6\u95F4\u3001\u5929\u6C14\u3001\u597D\u611F\u3001\u8EAB\u4F53\u72B6\u6001\u3001\u5F53\u524D\u5730\u70B9\u5404\u5360\u4E00\u683C\uFF0C\u53EF\u81EA\u5DF1\u51B3\u5B9A\u663E\u793A\u54EA\u4E9B\u3001\u653E\u5728\u54EA\u4E00\u680F\u3002\u5173\u6389\u5219\u56DE\u5230\u56FA\u5B9A\u6392\u7248\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u4FE1\u606F\u680F\u6539\u7528\u5C0F\u7EC4\u4EF6", checked: widgets, onChange: setWidgets })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u5173\u7CFB\u7F51\u81EA\u52A8\u8DF3\u8FC7\u5C55\u5F00\u52A8\u753B",
          desc: "\u8282\u70B9\u591A\u7684\u65F6\u5019\uFF0C\u957F\u679D\u6761\u52A8\u753B\u8981\u64AD\u5F88\u4E45\u624D\u6709\u53CD\u5E94\u3002\u8D85\u8FC7\u9608\u503C\u5C31\u76F4\u63A5\u51FA\u56FE\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u5173\u7CFB\u7F51\u81EA\u52A8\u8DF3\u8FC7\u5C55\u5F00\u52A8\u753B", checked: graphSkip, onChange: setGraphSkip })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: `\u8282\u70B9\u8D85\u8FC7 ${skipNodes.toFixed(0)} \u4E2A\u5C31\u8DF3\u8FC7`,
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            RangeSlider,
            {
              label: "\u8282\u70B9\u9608\u503C",
              min: 40,
              max: 1200,
              step: 1,
              value: skipNodes,
              onChange: setSkipNodes,
              width: 150,
              disabled: !graphSkip
            }
          )
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u5EFA\u6863\u5206\u6B65\u5F15\u5BFC",
          desc: "\u5F00\u65B0\u6863\u65F6\u4E00\u6B65\u4E00\u6B65\u95EE\uFF08\u4E16\u754C \u2192 \u4EBA\u8BBE \u2192 \u7EC6\u5219 \u2192 \u786E\u8BA4\uFF09\u3002\u5173\u6389\u5219\u4E00\u5C4F\u586B\u5B8C\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u5EFA\u6863\u5206\u6B65\u5F15\u5BFC", checked: saveOobe, onChange: setSaveOobe })
        }
      )
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(SettingCard, { title: "\u7740\u88C5\u7CFB\u7EDF", children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
      SettingRow,
      {
        title: "\u65B0\u7248\u7740\u88C5\uFF08\u5F62\u8C61\u5305 + \u670D\u88C5\u8F74\uFF09",
        desc: "\u6309\u90E8\u4EF6\u62FC\u7ACB\u7ED8\u3001\u6362\u914D\u8272\u4E0E\u670D\u88C5\uFF0C\u53EF\u8BA9 AI \u751F\u6210\u7D20\u6750\u5E76\u5F55\u5236\u52A8\u753B\u3002\u5173\u6389\u5219\u56DE\u5230\u65E7\u7248\uFF1A\u4E00\u5F20\u5F20\u9759\u6001\u7ACB\u7ED8\u6309\u8868\u60C5\u5207\u6362\u3002",
        control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u65B0\u7248\u7740\u88C5", checked: outfitNew, onChange: setOutfitNew })
      }
    ) }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u89D2\u8272\u4E0E\u5185\u5BB9", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u5141\u8BB8 AI \u968F\u610F\u52A0\u7684\u89D2\u8272\u5148\u9690\u53BB\u540D\u5B57",
          desc: "\u5BF9\u653E\u5F00\u89D2\u8272\u767D\u540D\u5355\u7684\u5B58\u6863\uFF1A\u65B0\u89D2\u8272\u767B\u573A\u65F6\u540D\u5B57\u663E\u793A\u4E3A\u300C\uFF1F\uFF1F\uFF1F\u300D\uFF0C\u7B49 TA \u81EA\u6211\u4ECB\u7ECD\u6216\u88AB\u4ECB\u7ECD\u4E4B\u540E\u624D\u663E\u793A\u771F\u540D\u3002\u5173\u6389\u5219\u76F4\u63A5\u663E\u793A\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u5148\u9690\u53BB\u540D\u5B57", checked: maskNames, onChange: setMaskNames })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u6027\u6B32\u7CFB\u7EDF",
          desc: "\u53EA\u5BF9\u4F60\u5728\u5B58\u6863\u91CC\u70B9\u540D\u7684\u89D2\u8272\u751F\u6548\uFF08\u4E3B\u89D2\u4E0E\u6307\u5B9A\u914D\u89D2\uFF09\uFF0C\u5176\u4F59\u89D2\u8272\u4E0D\u53C2\u4E0E\u3002\u6570\u503C\u7531 AI \u4E0E\u65F6\u95F4\u5171\u540C\u63A8\u8FDB\uFF1B\u4F60\u5728\u8BBE\u5B9A\u91CC\u5199\u6B7B\u7684\u503C\u4F18\u5148\u4E8E\u5468\u671F\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u6027\u6B32\u7CFB\u7EDF", checked: libido, onChange: setLibido })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: `\u751F\u7406\u5468\u671F\u5F71\u54CD ${Math.round(libidoCycle * 100)}%`,
          desc: "\u5468\u671F\u5BF9\u6027\u6B32\u7684\u62AC\u5347/\u538B\u4F4E\u5E45\u5EA6\u30020 = \u5B8C\u5168\u4E0D\u770B\u5468\u671F\uFF0C\u53EA\u6309\u8BBE\u5B9A\u4E0E\u60C5\u5883\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            GlassSlider,
            {
              label: "\u751F\u7406\u5468\u671F\u5F71\u54CD",
              width: 150,
              value: libidoCycle,
              onChange: setLibidoCycle,
              disabled: !libido
            }
          )
        }
      )
    ] })
  ] });
}
function PlatformSection() {
  const [discrete, setDiscrete] = useBool(false);
  const [phoneWindow, setPhoneWindow] = useBool(true);
  const [closeBehavior, setCloseBehavior] = useStr("\u6BCF\u6B21\u8BE2\u95EE");
  const [lowMemory, setLowMemory] = useBool(false);
  const [alwaysOnTop, setAlwaysOnTop] = useBool(false);
  const [petIdle, setPetIdle] = useStr("0");
  const [petThink, setPetThink] = useStr("\u5173\u95ED");
  const [petInterval, setPetInterval] = useStr("10");
  const [petScale, setPetScale] = useNum(1);
  const [crossScreen, setCrossScreen] = useBool(false);
  const [performance2, setPerformance] = useBool(false);
  const [complexPlot, setComplexPlot] = useBool(false);
  const [novelTesting, setNovelTesting] = useBool(false);
  const [showAffection, setShowAffection] = useBool(false);
  const [affectionUp, setAffectionUp] = useBool(true);
  const [lightning, setLightning] = useBool(true);
  const [lightningProb, setLightningProb] = useStr("0.06");
  const [rain, setRain] = useBool(true);
  const [rainProb, setRainProb] = useStr("0.15");
  const [lightningSfx, setLightningSfx] = useStr("");
  const [rainSfx, setRainSfx] = useStr("");
  const [lightningFrames, setLightningFrames] = useStr("");
  const [rainFrames, setRainFrames] = useStr("");
  const [frameMs, setFrameMs] = useStr("120");
  const [travelAlone, setTravelAlone] = useBool(false);
  const [allowFollow, setAllowFollow] = useBool(true);
  return /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(import_jsx_runtime46.Fragment, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(SettingCard, { title: "\u663E\u5361", children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
      SettingRow,
      {
        title: "\u4F18\u5148\u4F7F\u7528\u72EC\u663E",
        desc: "\u5728\u672C\u7A0B\u5E8F\u7684 Windows \u56FE\u5F62\u8BBE\u7F6E\u91CC\u767B\u8BB0\u300C\u9AD8\u6027\u80FD\u300D\uFF0C\u91CD\u542F\u7A0B\u5E8F\u540E\u751F\u6548\u3002\u5B83\u53EA\u5F71\u54CD\u73BB\u7483\u6A21\u7CCA\u3001\u5927\u56FE\u7F29\u653E\u8FD9\u7C7B\u5408\u6210\u5DE5\u4F5C\u2014\u2014\u4E4B\u524D\u91CF\u5230\u7684\u754C\u9762\u6784\u5EFA\u6162\u662F CPU \u4FA7\u7684\u4E8B\uFF0C\u6362\u663E\u5361\u5E2E\u4E0D\u4E0A\u3002",
        control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u4F18\u5148\u4F7F\u7528\u72EC\u663E", checked: discrete, onChange: setDiscrete })
      }
    ) }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u7A97\u53E3\u4E0E\u8FD0\u884C", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u624B\u673A\u5F00\u6210\u72EC\u7ACB\u7A97\u53E3",
          desc: "\u5F00\u7740\uFF1A\u4E3B\u7A97\u53E3\u7EE7\u7EED\u804A\u5929\uFF0C\u624B\u673A\u684C\u9762\u5728\u65C1\u8FB9\u5355\u5F00\u4E00\u6247\uFF08Windows\uFF09\u3002\u5173\u6389\uFF1A\u4ECD\u7136\u8DF3\u8F6C\u5230\u5E94\u7528\u5185\u7684\u624B\u673A\u9875\u3002\u624B\u673A\u4E0A\u59CB\u7EC8\u662F\u9875\u5185\u9875\u9762\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u624B\u673A\u5F00\u6210\u72EC\u7ACB\u7A97\u53E3", checked: phoneWindow, onChange: setPhoneWindow })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        PickRow,
        {
          title: "\u5173\u4E3B\u7A97\u53E3\u65F6\uFF0C\u5176\u4ED6\u7A97\u53E3\u600E\u4E48\u529E",
          desc: "\u300C\u6BCF\u6B21\u8BE2\u95EE\u300D\u4F1A\u5728\u771F\u6709\u5173\u7740\u7684\u7A97\u53E3\u65F6\u5F39\u4E00\u6B21\u9009\u62E9\uFF1B\u9009\u62E9\u65F6\u53EF\u52FE\u9009\u8BB0\u4F4F\uFF0C\u4E4B\u540E\u6765\u8FD9\u91CC\u6539\u56DE\u3002\u53EA\u5173\u4E3B\u7A97\u53E3\u65F6\u5176\u4ED6\u7A97\u53E3\u7EE7\u7EED\u5F00\uFF0C\u5168\u90E8\u5173\u5B8C\u7A0B\u5E8F\u624D\u9000\u51FA\u3002",
          value: closeBehavior,
          options: ["\u6BCF\u6B21\u8BE2\u95EE", "\u8FDE\u5176\u4ED6\u7A97\u53E3\u4E00\u8D77\u5173", "\u53EA\u5173\u4E3B\u7A97\u53E3"],
          onChange: setCloseBehavior,
          width: 160
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u8D85\u7701\u5185\u5B58\u6A21\u5F0F",
          desc: "\u8BB0\u5FC6\u3001\u5173\u7CFB\u7F51\u8BFB\u5B8C\u5C31\u4E22\u7F13\u5B58\uFF0C\u95F2\u7F6E\u89D2\u8272\u7684\u4F1A\u8BDD\u6E05\u7A7A\uFF0C\u542F\u52A8\u65F6\u4E0D\u518D\u540E\u53F0\u9884\u70ED\uFF1B\u4EE3\u4EF7\u662F\u4E0B\u6B21\u56DE\u5FC6\u8981\u591A\u8BFB\u4E00\u6B21\u76D8\u3002\u4F4E\u914D\u673A/\u5927\u5B58\u6863\u65F6\u6253\u5F00\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u8D85\u7701\u5185\u5B58\u6A21\u5F0F", checked: lowMemory, onChange: setLowMemory })
        }
      )
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u7535\u8111\u7AEF", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u7A97\u53E3\u7F6E\u9876",
          desc: "\u8BA9\u6E38\u620F\u7A97\u53E3\u59CB\u7EC8\u663E\u793A\u5728\u6700\u524D\u9762",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u7A97\u53E3\u7F6E\u9876", checked: alwaysOnTop, onChange: setAlwaysOnTop })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        PickRow,
        {
          title: "\u684C\u5BA0\u95F2\u7F6E\u65F6\u957F",
          desc: "0 \u5173\u95ED\uFF1B\u95F2\u7F6E\u6EE1\u65F6\u957F\u81EA\u52A8\u8FDB\u5165\u684C\u5BA0\uFF0C\u91CD\u65B0\u64CD\u4F5C\u65F6\u81EA\u52A8\u6062\u590D",
          value: petIdle,
          options: ["0", "1", "3", "5", "10", "15", "30", "60"],
          onChange: setPetIdle,
          width: 110
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        PickRow,
        {
          title: "\u684C\u5BA0\u601D\u8003",
          desc: "\u5173\u95ED=\u4E0D\u4E3B\u52A8\u5600\u5495\uFF1B\u968F\u673A=\u6309\u95F4\u9694\u63B7\u9AB0\u5B50\uFF08\u7EA6\u4E00\u534A\u6982\u7387\uFF09\uFF1B\u5B9A\u65F6=\u6309\u95F4\u9694\u5FC5\u60F3\u4E00\u6B21\u3002\u684C\u5BA0\u6A21\u5F0F\u4E0B\u5355\u51FB\u7ACB\u7ED8\u603B\u80FD\u89E6\u53D1\u4E00\u6B21",
          value: petThink,
          options: ["\u5173\u95ED", "\u968F\u673A", "\u5B9A\u65F6"],
          onChange: setPetThink,
          width: 110
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        PickRow,
        {
          title: "\u601D\u8003\u95F4\u9694",
          desc: "\u968F\u673A / \u5B9A\u65F6\u6A21\u5F0F\u4E0B\u6BCF\u9694\u591A\u4E45\u60F3\u4E00\u6B21\uFF08\u5206\u949F\uFF09",
          value: petInterval,
          options: ["1", "5", "10", "15", "30", "60"],
          onChange: setPetInterval,
          width: 110
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u684C\u5BA0\u4F53\u578B",
          desc: "\u5728\u300C\u5C4F\u5E55\u9AD8\u7684 42%\u300D\u8FD9\u4E2A\u57FA\u51C6\u4E0A\u518D\u4E58\u4E00\u5200\uFF1A\u5C4F\u8D8A\u5927\uFF0C\u57FA\u51C6\u9876\u5F97\u8D8A\u9AD8\uFF0C\u8FD9\u91CC\u624D\u662F\u771F\u6B63\u8C03\u5927\u5C0F\u7684\u5730\u65B9\u3002\u7528\u6EDA\u8F6E\u6216\u53CC\u6307\u634F\u8FC7\u4E4B\u540E\uFF0C\u8FD9\u91CC\u4F1A\u8DDF\u7740\u53D8",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("span", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-2)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
              RangeSlider,
              {
                label: "\u684C\u5BA0\u4F53\u578B",
                min: 0.4,
                max: 2.6,
                step: 0.05,
                value: petScale,
                onChange: setPetScale,
                width: 150
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("span", { className: "cap", style: { width: 42 }, children: [
              Math.round(petScale * 100),
              "%"
            ] })
          ] })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u5BF9\u8BDD\u5C0F\u7A97\u8DE8\u5C4F\u8DDF\u968F",
          desc: "\u591A\u663E\u793A\u5668\u7528\uFF1A\u5F00\u542F\u540E\u89D2\u8272\u62D6\u5230\u526F\u5C4F\uFF0C\u5934\u9876\u8F93\u5165\u6761\u8DDF\u7740\u8DE8\u8FC7\u53BB\uFF1B\u5173\u95ED\u65F6\u5C0F\u7A97\u53EA\u5728\u89D2\u8272\u90A3\u5757\u5C4F\u91CC\uFF0C\u8D34\u5230\u5C4F\u8FB9\u4F1A\u6298\u56DE\u672C\u5C4F\u3002\u5355\u5C4F\u65E0\u533A\u522B",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u5BF9\u8BDD\u5C0F\u7A97\u8DE8\u5C4F\u8DDF\u968F", checked: crossScreen, onChange: setCrossScreen })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u684C\u5BA0\u8BBE\u7F6E\u2026" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "AI \u5236\u4F5C\u684C\u5BA0\u2026" })
      ] })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(
      SettingCard,
      {
        title: "\u5DE5\u5177\u6A21\u5F0F\u8FD0\u884C\u65F6\uFF08Java / Python\uFF09",
        note: "\u5916\u90E8\u5DE5\u5177\u4F7F\u7528\u7EDF\u4E00\u7684 JSON-RPC \u534F\u8BAE\uFF0C\u7531 Java / Python \u89E3\u91CA\u5668\u6267\u884C\u3002\u7B56\u7565\uFF1A\u5148\u68C0\u6D4B\u672C\u673A \u2192 \u624B\u52A8\u6307\u5B9A\u8DEF\u5F84 \u2192 \u5FC5\u8981\u65F6\u81EA\u52A8\u4E0B\u8F7D\u4FBF\u643A\u8FD0\u884C\u65F6\u3002",
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { className: "s-row__text", children: [
              /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", style: { fontWeight: 700 }, children: "Python" }),
              /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "cap", children: "\u672A\u68C0\u6D4B\uFF08\u70B9\u51FB\u4E0B\u65B9\u300C\u68C0\u6D4B\u8FD0\u884C\u65F6\u300D\uFF09" })
            ] }),
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u68C0\u6D4B" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { className: "s-row__text", children: [
              /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", style: { fontWeight: 700 }, children: "Java" }),
              /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "cap", children: "\u672A\u68C0\u6D4B\uFF08\u70B9\u51FB\u4E0B\u65B9\u300C\u68C0\u6D4B\u8FD0\u884C\u65F6\u300D\uFF09" })
            ] }),
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u68C0\u6D4B" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--w-space-2)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u4E0B\u8F7D Python \u4FBF\u643A\u7248" }),
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u4E0B\u8F7D Java \u4FBF\u643A\u7248" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Divider, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "flex", alignItems: "flex-start", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { className: "s-row__text", children: [
              /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", style: { fontWeight: 700 }, children: "MCP \u4E0E\u63D2\u4EF6\u8981\u7528\u7684\u5916\u90E8\u8FD0\u884C\u65F6" }),
              /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__desc", children: "\u8FD8\u6CA1\u6709\u68C0\u6D4B\u8FC7\uFF1A\u70B9\u300C\u68C0\u6D4B\u300D\uFF0C\u770B\u8FD9\u53F0\u673A\u5668\u4E0A\u7F3A\u54EA\u4E00\u9879\u3002" })
            ] }),
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u68C0\u6D4B" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            Empty,
            {
              title: "\u8FD8\u6CA1\u5217\u51FA\u5916\u90E8\u8FD0\u884C\u65F6",
              reason: "\u8FD9\u4E00\u4EFD\u6E05\u5355\u8981\u8BFB\u8FC7\u672C\u673A\u73AF\u5883\u624D\u6709\u5185\u5BB9\u3002",
              hint: "\u5148\u70B9\u4E0A\u9762\u7684\u300C\u68C0\u6D4B\u300D\uFF0C\u7F3A\u54EA\u4E00\u9879\u4F1A\u5217\u5728\u8FD9\u91CC\u3002"
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Divider, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "cap", children: "Python \u8DEF\u5F84\uFF1A\u8FD8\u6CA1\u6709\u624B\u52A8\u6307\u5B9A" }),
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u8BBE\u7F6E\u8DEF\u5F84" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "cap", children: "Java \u8DEF\u5F84\uFF1A\u8FD8\u6CA1\u6709\u624B\u52A8\u6307\u5B9A" }),
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u8BBE\u7F6E\u8DEF\u5F84" })
          ] })
        ]
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u5B9E\u9A8C\u6027", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u6027\u80FD\u6A21\u5F0F",
          desc: "\u5F31\u673A\u3001\u8FDC\u7A0B\u684C\u9762\u6216\u9875\u9762\u6EDA\u52A8\u53D1\u6DA9\u65F6\u6253\u5F00\uFF1A\u5173\u6389\u8FDB\u573A/\u9875\u9762\u52A8\u6548\uFF0C\u5361\u7247\u9634\u5F71\u6539\u5E73\uFF1B\u529F\u80FD\u4E0E\u5E03\u5C40\u4E0D\u53D8\uFF0C\u968F\u65F6\u53EF\u4EE5\u5173\u6389\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u6027\u80FD\u6A21\u5F0F", checked: performance2, onChange: setPerformance })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u590D\u6742\u5267\u60C5\u6A21\u5F0F",
          desc: "\u542F\u7528\u6DF1\u5EA6\u601D\u8003\uFF0C\u589E\u52A0\u4F0F\u7B14",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u590D\u6742\u5267\u60C5\u6A21\u5F0F", checked: complexPlot, onChange: setComplexPlot })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u5C0F\u8BF4\u529F\u80FD\uFF08\u6D4B\u8BD5\uFF09",
          desc: "\u6D4B\u8BD5\u7528\u9014\uFF1A\u5BFC\u5165\u5C0F\u8BF4\u751F\u6210\u5267\u60C5\u6982\u8981\uFF0C\u4E0D\u53C2\u4E0E\u6B63\u5F0F\u6D41\u7A0B",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u5C0F\u8BF4\u529F\u80FD", checked: novelTesting, onChange: setNovelTesting })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u975E\u653B\u7565\u5BF9\u8C61\u663E\u793A\u597D\u611F",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u975E\u653B\u7565\u5BF9\u8C61\u663E\u793A\u597D\u611F", checked: showAffection, onChange: setShowAffection })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u597D\u611F\u7B49\u7EA7\u63D0\u5347\u52A8\u753B",
          desc: "\u6BCF 100 \u70B9\u597D\u611F\u5347 1 \u7EA7\uFF08\u5171 10 \u7EA7\uFF0C\u4E0A\u9650 1000 \u70B9\uFF09\uFF0C\u8DE8\u8D8A\u7B49\u7EA7\u65F6\u5168\u5C4F\u64AD\u653E\u7231\u5FC3\u52A8\u753B",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u597D\u611F\u7B49\u7EA7\u63D0\u5347\u52A8\u753B", checked: affectionUp, onChange: setAffectionUp })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Divider, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", children: "\u5929\u6C14\u4E8B\u4EF6\uFF08\u6253\u96F7 / \u4E0B\u96E8\uFF09" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u542F\u7528\u6253\u96F7\u4E8B\u4EF6",
          desc: "\u96E8\u5929/\u6781\u9634\u5929\u968F\u673A\u6253\u96F7\uFF1A\u5C4F\u5E55\u767D\u4E00\u4E0B\u518D\u6162\u6162\u6062\u590D\uFF0C\u53EF\u914D\u96F7\u58F0\u4E0E\u5E27\u52A8\u753B",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u542F\u7528\u6253\u96F7\u4E8B\u4EF6", checked: lightning, onChange: setLightning })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u6253\u96F7\u6982\u7387\uFF08\u57FA\u51C6\uFF0C0-1\uFF09",
          desc: "\u6253\u96F7\u5929\u81EA\u52A8\xD74\uFF0C\u96E8\u5929\u4E3A\u57FA\u51C6\u503C\uFF0C\u6781\u9634\u5929\xD70.3\u3002\u6570\u503C\u8D8A\u5927\u8D8A\u5BB9\u6613\u6253\u96F7",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { style: { display: "inline-block", width: 72 }, children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Field, { value: lightningProb, onChange: setLightningProb, type: "number", label: "\u6253\u96F7\u6982\u7387" }) })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u542F\u7528\u4E0B\u96E8\u4E8B\u4EF6",
          desc: "\u5929\u6C14\u4E3A\u96E8\u5929\u65F6\u968F\u673A\u89E6\u53D1\u96E8\u58F0/\u96E8\u89C6\u89C9",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u542F\u7528\u4E0B\u96E8\u4E8B\u4EF6", checked: rain, onChange: setRain })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u4E0B\u96E8\u6982\u7387\uFF080-1\uFF09",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { style: { display: "inline-block", width: 72 }, children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Field, { value: rainProb, onChange: setRainProb, type: "number", label: "\u4E0B\u96E8\u6982\u7387" }) })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u96F7\u58F0\u6587\u4EF6\u8DEF\u5F84",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { style: { display: "inline-block", width: 180 }, children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Field, { value: lightningSfx, onChange: setLightningSfx, label: "\u96F7\u58F0\u6587\u4EF6\u8DEF\u5F84" }) })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u96E8\u58F0\u6587\u4EF6\u8DEF\u5F84",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { style: { display: "inline-block", width: 180 }, children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Field, { value: rainSfx, onChange: setRainSfx, label: "\u96E8\u58F0\u6587\u4EF6\u8DEF\u5F84" }) })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingBlock, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", children: "\u6253\u96F7\u5E27\u56FE\uFF08\u6BCF\u884C\u4E00\u4E2A\u8DEF\u5F84\uFF0C\u53EF\u7559\u7A7A\uFF09" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "cap", children: "\u6309\u6B21\u5E8F\u5207\u6362\uFF1B\u53EA\u6709\u4E00\u5F20\u5219\u95EA\u4E00\u4E0B\uFF1B\u7559\u7A7A\u5219\u9ED8\u8BA4\u767D\u4E00\u4E0B\u518D\u6162\u6162\u6062\u590D" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Field, { value: lightningFrames, onChange: setLightningFrames, multiline: true, label: "\u6253\u96F7\u5E27\u56FE" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingBlock, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", children: "\u4E0B\u96E8\u5E27\u56FE\uFF08\u6BCF\u884C\u4E00\u4E2A\u8DEF\u5F84\uFF0C\u53EF\u7559\u7A7A\uFF09" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "cap", children: "\u5FAA\u73AF\u5207\u6362\uFF1B\u7559\u7A7A\u5219\u4E0D\u663E\u793A\u96E8\u89C6\u89C9\uFF08\u53EA\u6709\u96E8\u58F0\uFF09" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Field, { value: rainFrames, onChange: setRainFrames, multiline: true, label: "\u4E0B\u96E8\u5E27\u56FE" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u7279\u6548\u5E27\u5207\u6362\u95F4\u9694\uFF08\u6BEB\u79D2\uFF09",
          desc: "\u6253\u96F7\u767D\u95EA\u65F6\u957F / \u5E27\u56FE\u5207\u6362\u9891\u7387\uFF0C\u5EFA\u8BAE 80-300",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { style: { display: "inline-block", width: 72 }, children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Field, { value: frameMs, onChange: setFrameMs, type: "number", label: "\u5E27\u5207\u6362\u95F4\u9694" }) })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Divider, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", children: "\u884C\u8FDB\u6269\u5C55\uFF08AI \u51FA\u884C / \u8DDF\u968F\uFF09" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "AI \u53EF\u5355\u72EC\u51FA\u884C\uFF08\u4E0D\u5E26\u6211\uFF09",
          desc: "\u5F00\u542F\u540E AI \u53D1\u3010\u79FB\u52A8:\u3011\u53EA\u79FB\u52A8 AI \u81EA\u5DF1\uFF0C\u6211\u7684\u89C6\u89D2\u7559\u5728\u539F\u5730\uFF1B\u5173\u95ED\u5219\u7EF4\u6301\u65E7\u884C\u4E3A\uFF08AI \u8D70\u5230\u54EA\u6211\u8DDF\u5230\u54EA\uFF09",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "AI \u53EF\u5355\u72EC\u51FA\u884C", checked: travelAlone, onChange: setTravelAlone })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u5141\u8BB8\u6211\u300C\u8DDF\u4E0A\u300D",
          desc: "AI \u5355\u72EC\u8D70\u8FDC\u540E\uFF0C\u4E3B\u754C\u9762\u51FA\u73B0\u300C\u8DDF\u4E0A\u300D\u6309\u94AE\uFF1B\u70B9\u4E00\u4E0B\u5373\u53EF\u8DDF\u8FC7\u53BB",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u5141\u8BB8\u6211\u300C\u8DDF\u4E0A\u300D", checked: allowFollow, onChange: setAllowFollow, disabled: !travelAlone })
        }
      )
    ] })
  ] });
}
function IntegrationsSection() {
  const [qq, setQq] = useBool(false);
  const [qqAppId, setQqAppId] = useStr("");
  const [qqAppSecret, setQqAppSecret] = useStr("");
  const [qqReplyGroup, setQqReplyGroup] = useBool(false);
  const [qqOpenId, setQqOpenId] = useStr("");
  const [qqUserName, setQqUserName] = useStr("");
  const [qqProactive, setQqProactive] = useBool(false);
  const [qqConfirm, setQqConfirm] = useBool(true);
  const [qqPerHour, setQqPerHour] = useStr("3");
  const [outbound, setOutbound] = useStr("\u81EA\u52A8");
  const [wechat, setWechat] = useBool(false);
  const [wechatAppId, setWechatAppId] = useStr("");
  const [wechatAppSecret, setWechatAppSecret] = useStr("");
  const [wechatToken, setWechatToken] = useStr("");
  const [wechatPort, setWechatPort] = useStr("8012");
  const [channelsUrl, setChannelsUrl] = useStr("");
  const [mcp, setMcp] = useBool(false);
  const [mcpAuto, setMcpAuto] = useBool(true);
  const [pluginHooks, setPluginHooks] = useBool(true);
  const [bundlePlugins, setBundlePlugins] = useBool(true);
  const [crashLogs, setCrashLogs] = useBool(true);
  return /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(import_jsx_runtime46.Fragment, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(
      SettingCard,
      {
        title: "\u5B98\u65B9\u63A5\u5165\uFF08\u771F\u5FAE\u4FE1 / QQ\uFF09",
        note: "\u914D\u7F6E\u540E\uFF0C\u6E38\u620F\u91CC\u7684\u5FAE\u4FE1 / QQ \u5C06\u63A5\u5165\u4F60\u7684\u771F\u5B9E\u8D26\u53F7\uFF1A\u597D\u53CB\u53D1\u7ED9\u4F60\u7684\u6D88\u606F\u4F1A\u7531 TA \u4EB2\u81EA\u56DE\u590D\u3002\u5B98\u65B9\u65B9\u6848\u4EC5\u652F\u6301\u5355\u804A\uFF0C\u4E0D\u5360\u7528\u9F20\u6807\u952E\u76D8\uFF0C\u4E5F\u65E0\u9700\u989D\u5916\u5B89\u88C5\u4EFB\u4F55\u7A0B\u5E8F\u3002",
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(GroupLabel, { children: "QQ \u5B98\u65B9\u673A\u5668\u4EBA" }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            SettingRow,
            {
              title: "\u542F\u7528 QQ \u63A5\u5165",
              desc: "\u524D\u5F80 q.qq.com \u5F00\u653E\u5E73\u53F0\u521B\u5EFA\u673A\u5668\u4EBA",
              control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u542F\u7528 QQ \u63A5\u5165", checked: qq, onChange: setQq })
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "AppID", value: qqAppId, onChange: setQqAppId, placeholder: "\u673A\u5668\u4EBA AppID", width: 300 }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "AppSecret", value: qqAppSecret, onChange: setQqAppSecret, placeholder: "\u673A\u5668\u4EBA AppSecret", type: "password", width: 300 }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            PickRow,
            {
              title: "\u966A\u804A\u89D2\u8272",
              value: "\u8DDF\u968F\u5F53\u524D\u6E38\u620F\u89D2\u8272",
              options: ["\u8DDF\u968F\u5F53\u524D\u6E38\u620F\u89D2\u8272"],
              onChange: () => void 0,
              width: 220,
              label: "\u9009\u4E00\u4E2A\u89D2\u8272\uFF08\u5B58\u6863\uFF09"
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u63A5\u5165 QQ \u673A\u5668\u4EBA\u524D\u5148\u9009\u597D\u7528\u5B83\u966A\u804A\u7684\u89D2\u8272\uFF1B\u6CA1\u9009\u5219\u8DDF\u968F\u5F53\u524D\u6E38\u620F\u89D2\u8272\u3002" }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            SettingRow,
            {
              title: "\u56DE\u590D\u7FA4\u804A\u6D88\u606F",
              desc: "\u9ED8\u8BA4\u4E0D\u7406\u7FA4\u91CC\u7684\u6D88\u606F\uFF08\u514D\u5F97\u5728\u7FA4\u91CC\u5237\u5C4F\uFF09\uFF1B\u6253\u5F00\u540E\u53EA\u56DE\u5E94 @ \u5230\u673A\u5668\u4EBA\u7684\u6D88\u606F",
              control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u56DE\u590D\u7FA4\u804A\u6D88\u606F", checked: qqReplyGroup, onChange: setQqReplyGroup })
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            FieldRow,
            {
              title: "\u6211\u7684 QQ openid",
              value: qqOpenId,
              onChange: setQqOpenId,
              placeholder: "\u7559\u7A7A\u5219\u8BA4\u6700\u8FD1\u8DDF\u4F60\u79C1\u804A\u8FC7\u7684\u4EBA",
              width: 300
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "\u6211\u7684\u79F0\u547C", value: qqUserName, onChange: setQqUserName, placeholder: "\u89D2\u8272\u8FD9\u6837\u79F0\u547C\u4F60\uFF08\u5982\uFF1A\u5C0F\u6797\uFF09", width: 300 }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u586B\u4E86 openid \u5C31\u80FD\u8BA4\u51FA\u300C\u54EA\u4E2A\u662F\u4F60\u300D\uFF1B\u4E4B\u540E\u89D2\u8272\u53EF\u4EE5\u4E3B\u52A8\u7ED9\u4F60\u624B\u673A\u53D1\u6D88\u606F\u3002" }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            SettingRow,
            {
              title: "\u5141\u8BB8\u89D2\u8272\u4E3B\u52A8\u53D1\u6D88\u606F\u5230\u6211\u624B\u673A",
              desc: "\u5B58\u6863\u91CC TA\u300C\u62FF\u8D77\u624B\u673A\u53D1\u4E2A\u6D88\u606F\u300D\uFF0C\u5C31\u771F\u7684\u53D1\u5230\u4F60\u7684 QQ\u3002\u9ED8\u8BA4\u5173\u95ED",
              control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u5141\u8BB8\u89D2\u8272\u4E3B\u52A8\u53D1\u6D88\u606F", checked: qqProactive, onChange: setQqProactive })
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            SettingRow,
            {
              title: "\u53D1\u9001\u524D\u9700\u8981\u6211\u786E\u8BA4",
              desc: "\u5F00\u542F\u540E\u6BCF\u6761\u5916\u53D1\u6D88\u606F\u90FD\u5148\u6392\u961F\uFF0C\u4E3B\u754C\u9762\u70B9\u300C\u53D1\u51FA\u53BB\u300D\u624D\u771F\u7684\u53D1\u9001\uFF08AI \u4E0D\u80FD\u672A\u7ECF\u540C\u610F\u5F80\u5916\u53D1\uFF09",
              control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u53D1\u9001\u524D\u9700\u8981\u6211\u786E\u8BA4", checked: qqConfirm, onChange: setQqConfirm })
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            PickRow,
            {
              title: "\u6BCF\u5C0F\u65F6\u4E0A\u9650",
              desc: "\u9632\u6B62\u8BEF\u89E6\u6216\u88AB\u5237\u5C4F\uFF1B\u8D85\u51FA\u540E AI \u4F1A\u88AB\u544A\u77E5\u300C\u5148\u522B\u6253\u6270\u5BF9\u65B9\u300D",
              value: qqPerHour,
              options: ["1", "3", "5", "10"],
              onChange: setQqPerHour,
              width: 90
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            PickRow,
            {
              title: "\u8D70\u54EA\u4E2A\u901A\u9053",
              desc: "QQ \u4E0E\u5FAE\u4FE1\u7684\u8EAB\u4EFD\u662F\u4E24\u5957 openid\uFF1B\u81EA\u52A8 = \u8C01\u5728\u7EBF\u4E14\u8BA4\u8BC6\u4F60\u5C31\u8D70\u8C01",
              value: outbound,
              options: ["\u81EA\u52A8", "\u53EA\u8D70 QQ", "\u53EA\u8D70\u5FAE\u4FE1"],
              onChange: setOutbound,
              width: 110
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(GroupLabel, { children: "\u5FAE\u4FE1\u516C\u4F17\u53F7" }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            SettingRow,
            {
              title: "\u542F\u7528\u5FAE\u4FE1\u63A5\u5165",
              desc: "\u9700\u5728\u516C\u4F17\u53F7\u540E\u53F0\u914D\u7F6E\u670D\u52A1\u5668 URL\uFF08\u53EF\u7528\u5185\u7F51\u7A7F\u900F\uFF09\u6307\u5411\u672C\u673A",
              control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u542F\u7528\u5FAE\u4FE1\u63A5\u5165", checked: wechat, onChange: setWechat })
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "AppID", value: wechatAppId, onChange: setWechatAppId, placeholder: "\u516C\u4F17\u53F7 AppID", width: 300 }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "AppSecret", value: wechatAppSecret, onChange: setWechatAppSecret, placeholder: "\u516C\u4F17\u53F7 AppSecret", type: "password", width: 300 }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "Token", value: wechatToken, onChange: setWechatToken, placeholder: "\u670D\u52A1\u5668\u914D\u7F6E Token", width: 300 }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "\u56DE\u8C03\u7AEF\u53E3", value: wechatPort, onChange: setWechatPort, placeholder: "8012", type: "number", width: 140 }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            PickRow,
            {
              title: "\u966A\u804A\u89D2\u8272",
              value: "\u8DDF\u968F\u5F53\u524D\u6E38\u620F\u89D2\u8272",
              options: ["\u8DDF\u968F\u5F53\u524D\u6E38\u620F\u89D2\u8272"],
              onChange: () => void 0,
              width: 220,
              label: "\u9009\u4E00\u4E2A\u89D2\u8272\uFF08\u5B58\u6863\uFF09"
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u5FAE\u4FE1\u53EF\u4EE5\u7ED1\u53E6\u4E00\u4E2A\u89D2\u8272\uFF1B\u548C QQ \u9009\u540C\u4E00\u4E2A\u89D2\u8272\uFF0C\u5C31\u662F TA \u5728\u4E24\u4E2A\u5E73\u53F0\u4E0A\u540C\u65F6\u5728\u7EBF\u3002" }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            FieldRow,
            {
              title: "\u89C6\u9891\u53F7\u94FE\u63A5",
              value: channelsUrl,
              onChange: setChannelsUrl,
              placeholder: "https://\u2026\uFF08\u89C6\u9891\u53F7/\u9891\u9053\u4E3B\u9875\uFF0C\u7559\u7A7A\u5219\u58F3\u91CC\u70B9\u4E0D\u5F00\uFF09",
              width: 380
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { className: "s-row__text", children: [
              /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", children: "\u8FDE\u63A5\u72B6\u6001" }),
              /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "cap", children: "\u672A\u542F\u7528" })
            ] }),
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u6D4B\u8BD5\u8FDE\u63A5" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u6CE8\u610F\uFF1A\u5B98\u65B9\u65B9\u6848\u4E0D\u652F\u6301\u7FA4\u804A\uFF1B\u5982\u9700\u8981\u7FA4\u804A\uFF0C\u8BF7\u81EA\u884C\u63A5\u5165 MCP \u670D\u52A1\u5668\uFF0C\u4E0E\u672C\u5E94\u7528\u65E0\u5173\u3002" })
        ]
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "MCP \u914D\u7F6E", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "s-row__desc", children: "MCP \u662F\u5916\u90E8\u670D\u52A1\uFF0C\u4E0D\u662F\u6587\u4EF6\uFF1A\u8981\u4E48\u8BA9\u672C\u673A\u6309\u4E00\u6761\u547D\u4EE4\u628A\u670D\u52A1\u8D77\u8D77\u6765\uFF08stdio\uFF09\uFF0C\u8981\u4E48\u8FDE\u4E00\u4E2A\u5DF2\u7ECF\u8DD1\u7740\u7684\u5730\u5740\uFF08HTTP\uFF09\u3002\u767B\u8BB0\u65F6\u5199\u6E05\u695A\u4ECE\u54EA\u513F\u542F\u52A8\uFF0C\u63E1\u624B\u4E4B\u540E\u5B83\u81EA\u5DF1\u62A5\u5DE5\u5177\u6E05\u5355\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u542F\u7528 MCP",
          desc: "\u5F00\u542F\u540E\u53EF\u767B\u8BB0\u5E76\u8FDE\u63A5\u5916\u90E8 MCP \u670D\u52A1\uFF08\u672C\u673A\u8FDB\u7A0B stdio \u6216\u7F51\u7EDC\u5730\u5740 HTTP\uFF09\uFF1B\u63D2\u4EF6\u662F\u53E6\u4E00\u5957\u7CFB\u7EDF\uFF0C\u4E0D\u5728\u6B64\u5217",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u542F\u7528 MCP", checked: mcp, onChange: setMcp })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u81EA\u52A8\u786E\u8BA4",
          desc: "\u5F00\u542F\u540E\u811A\u672C/AI \u8C03\u7528 MCP \u5DE5\u5177\u65E0\u9700\u9010\u6B21\u4EBA\u5DE5\u786E\u8BA4",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u81EA\u52A8\u786E\u8BA4", checked: mcpAuto, onChange: setMcpAuto })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Divider, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", style: { fontWeight: 700 }, children: "\u5DF2\u63A5\u5165\u7684\u670D\u52A1" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("span", { style: { display: "flex", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u5237\u65B0" }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\uFF0B \u63A5\u5165\u670D\u52A1" })
        ] })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u8FD8\u6CA1\u6709\u63A5\u5165\u7684\u670D\u52A1\u3002\u70B9\u300C\uFF0B \u63A5\u5165\u670D\u52A1\u300D\u767B\u8BB0\u4E00\u4E2A\uFF1A\u672C\u673A\u8FDB\u7A0B\u8981\u7ED9\u542F\u52A8\u547D\u4EE4\uFF0C\u7F51\u7EDC\u670D\u52A1\u8981\u7ED9\u5730\u5740\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u4E0B\u9762\u5217\u51FA\u7684\u5DE5\u5177\u7531\u670D\u52A1\u81EA\u5DF1\u62A5\u4E0A\u6765\uFF1A\u63E1\u624B\u6210\u529F\u540E AI \u7528 mcp_list \u5C31\u80FD\u770B\u5230\u3002\u63D2\u4EF6\u4E0D\u5728\u8FD9\u91CC\u5B89\u88C5\u4E5F\u4E0D\u5728\u8FD9\u91CC\u663E\u793A\u2014\u2014\u63D2\u4EF6\u4E2D\u5FC3\u7BA1\u7684\u662F\u5E94\u7528\u5185\u6269\u5C55\uFF0C\u548C MCP \u662F\u4E24\u5957\u7CFB\u7EDF\u3002" })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u5DE5\u5177\u5B89\u5168 \xB7 \u6C99\u7BB1\u4FE1\u4EFB\u540D\u5355", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        Empty,
        {
          title: "\u8FD8\u6CA1\u6709\u4FE1\u4EFB\u7684\u5DE5\u5177\u76EE\u5F55",
          reason: "\u5916\u90E8\u5DE5\u5177\u9ED8\u8BA4\u8BFB\u4E0D\u5230\u6A21\u578B\u6570\u636E\uFF08API Key / \u5BF9\u8BDD\u5386\u53F2\uFF09\u3002",
          hint: "\u70B9\u4E0B\u9762\u7684\u300C\uFF0B \u4FE1\u4EFB\u4E00\u4E2A\u5DE5\u5177\u76EE\u5F55\u300D\u6311\u4E00\u4E2A\u76EE\u5F55\uFF0C\u52A0\u8FDB\u6765\u624D\u4F1A\u653E\u884C\u3002"
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(BtnRow, { children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\uFF0B \u4FE1\u4EFB\u4E00\u4E2A\u5DE5\u5177\u76EE\u5F55" }) }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u9ED8\u8BA4\u5916\u90E8\u5DE5\u5177\u65E0\u6CD5\u8BFB\u53D6\u6A21\u578B\u6570\u636E\uFF08API Key / \u5BF9\u8BDD\u5386\u53F2\uFF09\uFF1B\u52A0\u5165\u4FE1\u4EFB\u540E\u653E\u884C\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Divider, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", children: "\u5B8C\u6574\u6027\u68C0\u67E5" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u6838\u5BF9\u8BE5\u6709\u7684\u5DE5\u5177\u662F\u5426\u9F50\u5168\u3001\u63CF\u8FF0\u4E0E\u53C2\u6570 schema \u662F\u5426\u5B8C\u6574\u3001\u804A\u5929\u53EF\u7528\u5DE5\u5177\u96C6\u80FD\u5426\u6B63\u5E38\u6784\u5EFA\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(BtnRow, { children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u68C0\u67E5\u5DE5\u5177\u5B8C\u6574\u6027" }) }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Divider, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", children: "\u8868\u60C5\u5305\u5E93" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u5BFC\u5165\u7684\u8868\u60C5\u53EF\u4EE5\u5728\u5BF9\u8BDD\u91CC\u53D1\u9001\uFF1B\u5199\u4E0A\u63CF\u8FF0\uFF0CAI \u4E5F\u80FD\u6309\u573A\u5408\u6311\u8868\u60C5\u53D1\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(BtnRow, { children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\uFF0B \u5BFC\u5165\u8868\u60C5" }) }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        Empty,
        {
          title: "\u8868\u60C5\u5305\u5E93\u662F\u7A7A\u7684",
          reason: "\u8FD8\u6CA1\u6709\u5BFC\u5165\u8FC7\u8868\u60C5\uFF0C\u5217\u8868\u91CC\u6CA1\u6709\u53EF\u7528\u7684\u56FE\u7247\u3002",
          hint: "\u70B9\u300C\uFF0B \u5BFC\u5165\u8868\u60C5\u300D\u9009\u4E00\u5F20\uFF0C\u5BFC\u5165\u540E\u53EF\u4EE5\u5199\u63CF\u8FF0\u8BA9 AI \u6309\u573A\u5408\u6311\u3002"
        }
      )
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u63D2\u4EF6", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u542F\u7528\u63D2\u4EF6\u94A9\u5B50",
          desc: "\u63D2\u4EF6\u53EF\u4EE5\u6539\u4EBA\u8BBE\u3001\u6539\u5199\u53D1\u9001\u524D/\u56DE\u590D\u540E\u7684\u6587\u672C\u3001\u52A0\u8BBE\u7F6E\u9875\u3001\u52A0\u9875\u9762\u3001\u8C03\u7528\u5DF2\u6CE8\u518C\u7684\u5DE5\u5177\u3002\u94A9\u5B50\u8DD1\u5728\u53D7\u9650\u7684 JS \u6C99\u7BB1\u91CC\uFF08\u8D85\u65F6\u3001\u8BED\u53E5\u6570\u3001\u5185\u5B58\u90FD\u6709\u4E0A\u9650\uFF09\uFF1B\u5173\u6389\u540E\u6240\u6709\u63D2\u4EF6\u6548\u679C\u7ACB\u5373\u505C\u7528\uFF0C\u4F46\u6587\u4EF6\u4E0E\u8BBE\u7F6E\u4FDD\u7559\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u542F\u7528\u63D2\u4EF6\u94A9\u5B50", checked: pluginHooks, onChange: setPluginHooks })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u5B58\u6863\u8FDE\u540C\u63D2\u4EF6\u4E00\u8D77\u6253\u5305",
          desc: "\u5BFC\u51FA\u5B58\u6863\u65F6\u628A\u63D2\u4EF6\u4EE3\u7801\u4E0E\u8BBE\u7F6E\u4E00\u5E76\u5E26\u8D70\uFF0C\u6362\u8BBE\u5907\u4E0D\u4F1A\u51FA\u73B0\u300C\u6709\u6570\u636E\u6CA1\u7A0B\u5E8F\u300D\u3002\u63D2\u4EF6\u6587\u4EF6\u8F83\u5927\u65F6\u4F1A\u8BA9\u5B58\u6863\u53D8\u5927\uFF1B\u654F\u611F\u5BC6\u94A5\uFF08\u4E91\u5B58\u6863\u4EE4\u724C\uFF09\u4E0D\u4F1A\u8FDB\u5305\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u5B58\u6863\u8FDE\u540C\u63D2\u4EF6\u4E00\u8D77\u6253\u5305", checked: bundlePlugins, onChange: setBundlePlugins })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u8BB0\u5F55\u63D2\u4EF6\u5D29\u6E83\u65E5\u5FD7",
          desc: "\u63D2\u4EF6\u94A9\u5B50\u629B\u9519\u65F6\u5199\u65E5\u5FD7\uFF08\u5355\u6587\u4EF6 256KB \u8F6E\u8F6C\uFF09\u5E76\u5F39\u51FA\u53EF\u67E5\u770B\u7684\u539F\u56E0\uFF1B\u5173\u95ED\u540E\u53EA\u9759\u9ED8\u8DF3\u8FC7\u3002\u65E0\u8BBA\u5F00\u5173\u4E0E\u5426\uFF0C\u4E00\u4E2A\u63D2\u4EF6\u7684\u9519\u8BEF\u4E0D\u4F1A\u62D6\u57AE\u4E3B\u7A0B\u5E8F\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u8BB0\u5F55\u63D2\u4EF6\u5D29\u6E83\u65E5\u5FD7", checked: crashLogs, onChange: setCrashLogs })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", style: { fontWeight: 700 }, children: "\u5DF2\u5B89\u88C5\u7684\u63D2\u4EF6" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("span", { style: { display: "flex", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u5237\u65B0" }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u6253\u5F00\u63D2\u4EF6\u4E2D\u5FC3" })
        ] })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u8FD8\u6CA1\u6709\u5B89\u88C5\u63D2\u4EF6\u3002\u63D2\u4EF6\u4E2D\u5FC3\u80FD\u88C5 zip / \u6587\u4EF6\u5939\uFF0C\u4E5F\u80FD\u8BA9 AI \u6309\u4F60\u7684\u60F3\u6CD5\u73B0\u573A\u5199\u4E00\u4E2A\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u63D2\u4EF6\u662F\u5E94\u7528\u5185\u7684\u5168\u6808\u6269\u5C55\uFF1A\u6539\u4EBA\u8BBE\u4E0E\u56DE\u590D\u3001\u52A0\u8BBE\u7F6E\u9875\u3001\u52A0\u9875\u9762\u3001\u8C03\u5DE5\u5177\uFF1BMCP \u662F\u5916\u90E8\u670D\u52A1\u5DE5\u5177\uFF08\u672C\u673A\u8FDB\u7A0B\u6216\u7F51\u7EDC\u5730\u5740\uFF09\u3002\u4E24\u5957\u7CFB\u7EDF\u5206\u5F00\u5B89\u88C5\uFF0C\u63D2\u4EF6\u4E0D\u4F1A\u51FA\u73B0\u5728 MCP \u5217\u8868\u91CC\uFF0CMCP \u4E5F\u4E0D\u4F1A\u5360\u7528\u63D2\u4EF6\u76EE\u5F55\u3002" })
    ] })
  ] });
}
function LifeSection() {
  const [notify, setNotify] = useBool(true);
  const [greeting, setGreeting] = useBool(true);
  const [citySearch, setCitySearch] = useStr("");
  const [chooserOpen, setChooserOpen] = useBool(false);
  const [weatherSource, setWeatherSource] = useStr("Open-Meteo\uFF08\u9ED8\u8BA4\uFF0C\u514D\u5BC6\u94A5\uFF09");
  const [weatherUrl, setWeatherUrl] = useStr("");
  const [weatherKey, setWeatherKey] = useStr("");
  const [cycleGender, setCycleGender] = useStr("\u5973");
  const [cycleTracked, setCycleTracked] = useBool(false);
  const [cycleLength, setCycleLength] = useStr("28");
  const [periodLength, setPeriodLength] = useStr("5");
  const [cycleAdvanced, setCycleAdvanced] = useBool(false);
  const [toast, setToast] = useBool(true);
  const [toastPosition, setToastPosition] = useStr("\u53F3\u4E0A\u89D2\uFF08\u9ED8\u8BA4\uFF09");
  const [toastSeconds, setToastSeconds] = useNum(4);
  const [runtime, setRuntime] = useBool(false);
  const [runtimeAi, setRuntimeAi] = useBool(true);
  const [runtimeAiInterval, setRuntimeAiInterval] = useStr("\u6BCF 20 \u5206\u949F");
  const [runtimeGames, setRuntimeGames] = useBool(true);
  const [runtimeProactive, setRuntimeProactive] = useBool(false);
  const [runtimeBudget, setRuntimeBudget] = useStr("\u6BCF\u5C0F\u65F6 6 \u6B21");
  const [runtimeQuiet, setRuntimeQuiet] = useBool(true);
  return /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(import_jsx_runtime46.Fragment, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u901A\u77E5\u4E0E\u966A\u4F34", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u6D88\u606F\u901A\u77E5",
          desc: "\u771F\u5B9E\u5FAE\u4FE1 / QQ \u6D88\u606F\u5230\u8FBE\u65F6\u63D0\u793A",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u6D88\u606F\u901A\u77E5", checked: notify, onChange: setNotify })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u7A7A\u95F2\u95EE\u5019",
          desc: "TA \u4F1A\u5728\u7A7A\u95F2\u65F6\u4E3B\u52A8\u95EE\u5019\u4F60",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u7A7A\u95F2\u95EE\u5019", checked: greeting, onChange: setGreeting })
        }
      )
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u5929\u6C14\u4E0E\u751F\u6D3B", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(
        "div",
        {
          style: {
            display: "flex",
            alignItems: "flex-start",
            justifyContent: "space-between",
            gap: "var(--w-space-3)",
            padding: "13px 0"
          },
          children: [
            /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { className: "s-row__text", style: { gap: 2 }, children: [
              /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", children: "\u6240\u5728\u57CE\u5E02" }),
              /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { style: { fontSize: 15, color: "var(--w-ink-body)" }, children: "\u672A\u8BBE\u7F6E\uFF08\u9ED8\u8BA4\u5317\u4EAC\uFF09" }),
              /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "cap", children: "\u8FD8\u6CA1\u6709\u5750\u6807" })
            ] }),
            /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("span", { style: { display: "flex", gap: "var(--w-space-2)" }, children: [
              /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u5B9A\u4F4D" }),
              /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { onClick: () => setChooserOpen(!chooserOpen), children: "\u9009\u62E9\u57CE\u5E02" })
            ] })
          ]
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u5B9A\u4F4D\u4F18\u5148\u7528\u8BBE\u5907 GPS\uFF08\u7535\u8111\u8D70 Windows \u5B9A\u4F4D\u670D\u52A1\uFF09\uFF1BIP \u53EA\u505A\u5019\u9009\uFF0C\u5FC5\u987B\u4F60\u786E\u8BA4\u624D\u4F1A\u751F\u6548\u3002" }),
      chooserOpen ? /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingBlock, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Field, { value: citySearch, onChange: setCitySearch, placeholder: "\u8F93\u5165\u57CE\u5E02\u540D\u641C\u7D22\uFF0C\u5982\uFF1A\u676D\u5DDE", label: "\u57CE\u5E02\u641C\u7D22" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "cap", children: "\u70ED\u95E8\u57CE\u5E02\uFF08\u4E0D\u8054\u7F51\u4E5F\u80FD\u9009\uFF09" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
          Empty,
          {
            title: "\u8FD8\u6CA1\u6709\u57CE\u5E02\u5217\u8868",
            reason: "\u70ED\u95E8\u57CE\u5E02\u7531\u540E\u7AEF\u4E0B\u53D1\uFF0C\u8FD9\u91CC\u8FD8\u6CA1\u8BFB\u5230\u3002",
            hint: "\u5148\u5728\u641C\u7D22\u6846\u8F93\u5165\u4E24\u4E2A\u5B57\uFF0C\u7B49\u8054\u60F3\u51FA\u6765\u518D\u9009\u3002"
          }
        )
      ] }) : null,
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        PickRow,
        {
          title: "\u5929\u6C14\u6570\u636E\u6E90",
          desc: "\u9ED8\u8BA4 Open-Meteo\uFF0C\u53EF\u6362 wttr.in \u6216\u81EA\u5B9A\u4E49\u5730\u5740",
          value: weatherSource,
          options: ["Open-Meteo\uFF08\u9ED8\u8BA4\uFF0C\u514D\u5BC6\u94A5\uFF09", "wttr.in\uFF08\u514D\u5BC6\u94A5\uFF09", "\u81EA\u5B9A\u4E49\uFF08Open-Meteo \u517C\u5BB9\uFF09"],
          onChange: setWeatherSource,
          width: 180
        }
      ),
      weatherSource === "\u81EA\u5B9A\u4E49\uFF08Open-Meteo \u517C\u5BB9\uFF09" ? /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingBlock, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "\u63A5\u53E3\u5730\u5740", value: weatherUrl, onChange: setWeatherUrl, placeholder: "https://example.com", width: 320 }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "\u5BC6\u94A5", value: weatherKey, onChange: setWeatherKey, placeholder: "sk-\u2026", type: "password", width: 320 }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u81EA\u5B9A\u4E49\u5730\u5740\u6309 Open-Meteo \u534F\u8BAE\u8BF7\u6C42\uFF08\u5730\u5740 + /v1/forecast?latitude=\u2026&longitude=\u2026\uFF09\uFF1B\u5BC6\u94A5\u53EA\u53D1\u7ED9\u8FD9\u4E2A\u5730\u5740\uFF0C\u6309 Authorization: Bearer \u5934\u5E26\u4E0A" })
      ] }) : null,
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(BtnRow, { children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u6D4B\u8BD5\u53D6\u4E00\u6B21\u5929\u6C14" }) }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        PickRow,
        {
          title: "\u9002\u7528\u6027\u522B",
          desc: "\u5148\u9009\u6027\u522B\uFF0C\u518D\u7ED9\u8FD9\u4E2A\u6027\u522B\u914D\u5468\u671F",
          value: cycleGender,
          options: ["\u5973", "\u7537", "\u5176\u4ED6"],
          onChange: setCycleGender,
          width: 110
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u8FD9\u4E2A\u6027\u522B\u6709\u751F\u7406\u5468\u671F",
          desc: "\u5173\u6389\u540E\uFF0C\u63D0\u793A\u8BCD\u548C\u754C\u9762\u90FD\u4E0D\u518D\u51FA\u73B0\u7ECF\u671F\u8868\u8FF0",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u8FD9\u4E2A\u6027\u522B\u6709\u751F\u7406\u5468\u671F", checked: cycleTracked, onChange: setCycleTracked })
        }
      ),
      cycleTracked ? /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingBlock, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
          PickRow,
          {
            title: "\u751F\u7406\u5468\u671F\u5929\u6570",
            desc: "TA \u7684\u4E00\u8F6E\u751F\u7406\u5468\u671F\u957F\u5EA6",
            value: cycleLength,
            options: ["21", "22", "23", "24", "25", "26", "27", "28", "29", "30", "31", "32", "33", "34", "35"],
            onChange: setCycleLength,
            width: 110
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
          PickRow,
          {
            title: "\u7ECF\u671F\u5929\u6570",
            desc: "\u6BCF\u6708\u7279\u6B8A\u65E5\u5B50\u7684\u957F\u5EA6",
            value: periodLength,
            options: ["3", "4", "5", "6", "7"],
            onChange: setPeriodLength,
            width: 110
          }
        )
      ] }) : null,
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u9AD8\u7EA7\uFF1A\u81EA\u5B9A\u4E49\u6027\u522B\u7684\u5468\u671F",
          desc: "\u51FA\u5382\u53EA\u7ED9\u300C\u5973\u300D\u914D\u4E86\u5468\u671F\uFF1B\u5176\u4ED6\u6027\u522B\u8981\u6709\u5468\u671F\uFF0C\u5F97\u5728\u8FD9\u91CC\u65B0\u5EFA\u4E00\u6761\u89C4\u5219",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u9AD8\u7EA7\uFF1A\u81EA\u5B9A\u4E49\u6027\u522B\u7684\u5468\u671F", checked: cycleAdvanced, onChange: setCycleAdvanced })
        }
      ),
      cycleAdvanced ? /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingBlock, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("div", { children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u6DFB\u52A0\u6027\u522B\u89C4\u5219\u2026" }) }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u65B0\u52A0\u7684\u89C4\u5219\u9ED8\u8BA4\u300C\u65E0\u5468\u671F\u300D\uFF0C\u9009\u4E2D\u5B83\u518D\u6253\u5F00\u4E0A\u9762\u7684\u5F00\u5173" })
      ] }) : null
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u6D6E\u52A8\u901A\u77E5", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u7528\u6D6E\u52A8\u901A\u77E5\u4EE3\u66FF\u5F39\u7A97",
          desc: "\u4FDD\u5B58\u3001\u540C\u6B65\u3001\u5BFC\u5165\u8FD9\u7C7B\u7ED3\u679C\u4E0D\u518D\u5F39\u7A97\u6321\u4F4F\u6309\u952E\uFF1A\u4ECE\u5C4F\u5E55\u8FB9\u7F18\u6ED1\u8FDB\u6765\uFF0C\u51E0\u79D2\u540E\u81EA\u5DF1\u6ED1\u56DE\u53BB\u3002\u9700\u8981\u4F60\u786E\u8BA4\u7684\u4E8B\uFF08\u5220\u9664\u5B58\u6863\u3001\u8986\u76D6\u6587\u4EF6\uFF09\u4ECD\u7136\u7528\u5BF9\u8BDD\u6846\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u7528\u6D6E\u52A8\u901A\u77E5\u4EE3\u66FF\u5F39\u7A97", checked: toast, onChange: setToast })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        PickRow,
        {
          title: "\u51FA\u73B0\u4F4D\u7F6E",
          desc: "\u9ED8\u8BA4\u53F3\u4E0A\u89D2\uFF1A\u8D34\u7740\u8FB9\u7F18\u4F46\u4E0D\u538B\u4F4F\u9876\u680F\u6309\u94AE\u3002",
          value: toastPosition,
          options: ["\u53F3\u4E0A\u89D2\uFF08\u9ED8\u8BA4\uFF09", "\u5DE6\u4E0A\u89D2", "\u53F3\u4E0B\u89D2", "\u5DE6\u4E0B\u89D2", "\u9876\u90E8\u5C45\u4E2D", "\u5E95\u90E8\u5C45\u4E2D", "\u5C4F\u5E55\u4E2D\u95F4"],
          onChange: setToastPosition,
          width: 150,
          disabled: !toast
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: `\u505C\u7559 ${Number(toastSeconds.toFixed(1))} \u79D2`,
          desc: "\u4ECE\u6ED1\u5165\u5230\u6ED1\u8D70\u7684\u505C\u7559\u65F6\u957F\uFF1B\u6ED1\u5165\u6ED1\u8D70\u672C\u8EAB\u53E6\u6709 0.2 \u79D2\u5DE6\u53F3\u3002",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            RangeSlider,
            {
              label: "\u505C\u7559\u65F6\u957F",
              min: 1.5,
              max: 12,
              step: 0.1,
              value: toastSeconds,
              onChange: setToastSeconds,
              width: 150,
              disabled: !toast
            }
          )
        }
      )
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(
      SettingCard,
      {
        title: "\u5B58\u6863\u8FD0\u884C\u65F6",
        note: "\u5B58\u6863\u6253\u5F00\u540E\uFF0C\u8FD9\u4E00\u6863\u91CC\u7684\u89D2\u8272\u90FD\u5728\u540E\u53F0\u7EE7\u7EED\u6D3B\u7740\uFF1A\u6709\u81EA\u5DF1\u7684\u4F5C\u606F\uFF0C\u4F1A\u81EA\u5DF1\u4E0B\u68CB\u3001\u73A9\u5C0F\u6E38\u620F\uFF0C\u4F1A\u7528\u81EA\u5DF1\u7684\u6A21\u578B\u5199\u4E0B\u6B64\u523B\u7684\u751F\u6D3B\u7247\u6BB5\uFF0C\u4E5F\u4F1A\u5728\u4F60\u60F3\u804A\u7684\u65F6\u5019\u63A5\u7740\u521A\u624D\u7684\u4E8B\u8BF4\u4E0B\u53BB\u3002\u56DE\u6807\u9898\u65F6\u8FD9\u4E9B\u72B6\u6001\u4E00\u8D77\u5B58\u8FDB\u8FD9\u4E00\u6863\u3002",
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            SettingRow,
            {
              title: "\u5B58\u6863\u6253\u5F00\u65F6\u8BA9\u89D2\u8272\u7EE7\u7EED\u8FD0\u884C",
              desc: "\u5173\u6389\u5219\u4E00\u5207\u7167\u65E7\uFF1A\u53EA\u6709\u4F60\u4E3B\u52A8\u627E TA \u65F6\u89D2\u8272\u624D\u4F1A\u52A8\u3002",
              control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u5B58\u6863\u6253\u5F00\u65F6\u8BA9\u89D2\u8272\u7EE7\u7EED\u8FD0\u884C", checked: runtime, onChange: setRuntime })
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            SettingRow,
            {
              title: "\u8BA9 TA \u7528\u6A21\u578B\u5199\u751F\u6D3B\u7247\u6BB5",
              desc: "\u89D2\u8272\u7528\u81EA\u5DF1\u7684\u6A21\u578B\u5199\u4E00\u5C0F\u6BB5\u6B64\u523B\u5728\u505A\u4EC0\u4E48\uFF0C\u8BB0\u8FDB TA \u7684\u8BB0\u5FC6\u3002\u5173\u6389\u53EA\u505A\u672C\u5730\u4F5C\u606F\u63A8\u6F14\uFF0C\u4E0D\u82B1 token\u3002",
              control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u8BA9 TA \u7528\u6A21\u578B\u5199\u751F\u6D3B\u7247\u6BB5", checked: runtimeAi, onChange: setRuntimeAi, disabled: !runtime })
            }
          ),
          runtimeAi ? /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            PickRow,
            {
              title: "\u751F\u6D3B\u56DE\u5408\u95F4\u9694",
              desc: "\u6BCF\u4E2A\u89D2\u8272\u5E73\u5747\u591A\u4E45\u5199\u4E00\u6B21\uFF1B\u5B9E\u9645\u4F1A\u6309\u89D2\u8272\u9519\u5F00\u3002",
              value: runtimeAiInterval,
              options: ["\u6BCF 10 \u5206\u949F", "\u6BCF 15 \u5206\u949F", "\u6BCF 20 \u5206\u949F", "\u6BCF 30 \u5206\u949F", "\u6BCF 45 \u5206\u949F", "\u6BCF 60 \u5206\u949F"],
              onChange: setRuntimeAiInterval,
              width: 120,
              disabled: !runtime
            }
          ) : null,
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            SettingRow,
            {
              title: "\u8BA9 TA \u81EA\u5DF1\u4E0B\u68CB\u3001\u73A9\u5C0F\u6E38\u620F",
              desc: "\u672C\u5730\u7ED3\u7B97\uFF0C\u4E0D\u82B1 token\uFF1B\u53EA\u5199\u4E00\u6761\u751F\u6D3B\u8BB0\u5FC6\uFF0C\u4E0D\u5F71\u54CD\u4F60\u4E0E TA \u5BF9\u5F08\u65F6\u7684\u68CB\u529B\u8BB0\u5F55\u3002",
              control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u8BA9 TA \u81EA\u5DF1\u4E0B\u68CB\u3001\u73A9\u5C0F\u6E38\u620F", checked: runtimeGames, onChange: setRuntimeGames, disabled: !runtime })
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            SettingRow,
            {
              title: "\u5141\u8BB8 TA \u4E3B\u52A8\u7ED9\u4F60\u53D1\u6D88\u606F",
              desc: "\u8FD8\u8981\u5728\u300C\u5916\u90E8\u63A5\u5165 \u2192 QQ/\u5FAE\u4FE1\u300D\u91CC\u6253\u5F00\u300C\u5141\u8BB8\u89D2\u8272\u4E3B\u52A8\u53D1\u6D88\u606F\u300D\uFF0C\u5E76\u4E14\u8BA9 TA \u8BA4\u8BC6\u4F60\u3002\u53EA\u6709\u7ED1\u5B9A\u8BE5\u901A\u9053\u7684\u89D2\u8272\u624D\u4F1A\u53D1\u3002",
              control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u5141\u8BB8 TA \u4E3B\u52A8\u7ED9\u4F60\u53D1\u6D88\u606F", checked: runtimeProactive, onChange: setRuntimeProactive, disabled: !runtime })
            }
          ),
          runtimeProactive ? /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            PickRow,
            {
              title: "\u540E\u53F0\u6BCF\u5C0F\u65F6\u6700\u591A\u8DD1\u51E0\u6B21 AI",
              desc: "\u6240\u6709\u89D2\u8272\u5171\u7528\u8FD9\u4E2A\u9884\u7B97\uFF0C\u907F\u514D\u540E\u53F0\u628A\u989D\u5EA6\u5403\u5149\u3002",
              value: runtimeBudget,
              options: ["\u6BCF\u5C0F\u65F6 3 \u6B21", "\u6BCF\u5C0F\u65F6 6 \u6B21", "\u6BCF\u5C0F\u65F6 12 \u6B21", "\u6BCF\u5C0F\u65F6 20 \u6B21"],
              onChange: setRuntimeBudget,
              width: 120,
              disabled: !runtime
            }
          ) : null,
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
            SettingRow,
            {
              title: "\u6DF1\u591C\u5B89\u9759\uFF0823:00\u20137:00\uFF09",
              desc: "\u8FD9\u6BB5\u65F6\u95F4\u53EA\u6362\u4F5C\u606F\uFF0C\u4E0D\u5199\u751F\u6D3B\u7247\u6BB5\u3001\u4E0D\u4E0B\u68CB\u3001\u4E0D\u53D1\u6D88\u606F\uFF0C\u4E5F\u4E0D\u82B1 token\u3002",
              control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u6DF1\u591C\u5B89\u9759", checked: runtimeQuiet, onChange: setRuntimeQuiet, disabled: !runtime })
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { className: "s-row__text", children: [
              /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "s-row__title", children: "\u89D2\u8272\u6B64\u523B\u5728\u505A\u4EC0\u4E48" }),
              /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "cap", children: "\u8FD8\u6CA1\u6709\u89D2\u8272\u52A8\u8D77\u6765\uFF0C\u8FDB\u5B58\u6863\u5F85\u4E00\u4F1A\u513F\u518D\u6765\u770B\u770B\u3002" })
            ] }),
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u5237\u65B0" })
          ] })
        ]
      }
    )
  ] });
}
function DataSection() {
  const [includeSecrets, setIncludeSecrets] = useBool(false);
  const [cloud, setCloud] = useBool(false);
  const [cloudRepo, setCloudRepo] = useStr("");
  const [cloudToken, setCloudToken] = useStr("");
  const [cloudDevice, setCloudDevice] = useStr("");
  const [cloudAuto, setCloudAuto] = useBool(true);
  const [cloudReconcile, setCloudReconcile] = useBool(true);
  const [beta, setBeta] = useBool(false);
  return /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(import_jsx_runtime46.Fragment, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(
      SettingCard,
      {
        title: "\u6570\u636E\u7BA1\u7406",
        note: "\u5BFC\u51FA / \u5BFC\u5165\u6574\u4E2A\u5E94\u7528\u6570\u636E\uFF08\u7ACB\u7ED8\u3001\u5B58\u6863\u3001\u89D2\u8272\u3001\u80CC\u666F\u7B49\uFF09\u3002\u9ED8\u8BA4\u4E0D\u542B API \u5BC6\u94A5\u4E0E\u673A\u5668\u4EBA\u5BC6\u94A5\uFF0C\u53EF\u5728\u4E0B\u65B9\u52FE\u9009\u3002",
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--w-space-2)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(SignalButton, { children: "\u5BFC\u51FA\u6570\u636E\u5305" }),
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u5BFC\u5165\u6570\u636E\u5305" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-2)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "cap", children: "\u5305\u542B API \u5BC6\u94A5\u4E0E\u673A\u5668\u4EBA\u5BC6\u94A5" }),
            /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u5305\u542B API \u5BC6\u94A5\u4E0E\u673A\u5668\u4EBA\u5BC6\u94A5", checked: includeSecrets, onChange: setIncludeSecrets })
          ] })
        ]
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u4E91\u5B58\u6863", note: "\u628A\u5B58\u6863\u3001\u89D2\u8272\u4E0E\u804A\u5929\u8BB0\u5F55\u955C\u50CF\u5230\u4F60\u81EA\u5DF1\u7684 GitHub \u4ED3\u5E93\u3002\u4E24\u53F0\u8BBE\u5907\u5404\u5199\u5404\u7684\u76EE\u5F55\uFF0C\u6309\u65F6\u95F4\u53D6\u65B0\uFF0C\u4E0D\u4F1A\u4E92\u76F8\u8986\u76D6\uFF1B\u540C\u65F6\u4F7F\u7528\u6700\u574F\u4E5F\u53EA\u662F\u65E7\u7684\u4E00\u4EFD\u88AB\u653E\u8FDB conflicts \u5907\u4EFD\u3002", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(SettingBlock, { children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("span", { className: "cap", style: { lineHeight: 1.5 }, children: "\u53EA\u63A5\u53D7\u79C1\u4EBA\u4ED3\u5E93\uFF08Private\uFF09\uFF1A\u516C\u5F00\u4ED3\u5E93\u4F1A\u88AB\u62D2\u7EDD\u540C\u6B65\uFF1B\u8BBF\u95EE\u4EE4\u724C\u9700\u8981\u6709 repo\uFF08\u79C1\u6709\u4ED3\u5E93\u8BFB\u5199\uFF09\u6743\u9650\uFF0C\u4E14\u53EA\u4FDD\u5B58\u5728\u672C\u673A\u3002" }) }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u542F\u7528\u4E91\u5B58\u6863",
          desc: "\u5173\u95ED\u65F6\u53EA\u5728\u672C\u673A\u4FDD\u5B58\uFF1B\u5F00\u542F\u540E\u6539\u52A8\u4F1A\u81EA\u52A8\u4E0A\u4F20",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u542F\u7528\u4E91\u5B58\u6863", checked: cloud, onChange: setCloud })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "\u4ED3\u5E93", value: cloudRepo, onChange: setCloudRepo, placeholder: "owner/repo\uFF08\u5EFA\u8BAE\u79C1\u6709\u4ED3\u5E93\uFF09", width: 300 }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        FieldRow,
        {
          title: "\u8BBF\u95EE\u4EE4\u724C",
          value: cloudToken,
          onChange: setCloudToken,
          placeholder: "github_pat_\u2026\uFF08\u53EA\u9700 Contents \u8BFB\u5199\uFF09",
          type: "password",
          width: 300
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(FieldRow, { title: "\u8BBE\u5907\u540D", value: cloudDevice, onChange: setCloudDevice, placeholder: "\u5BB6\u91CC\u7535\u8111 / \u6211\u7684\u624B\u673A", width: 260 }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u6539\u52A8\u540E\u81EA\u52A8\u4E0A\u4F20",
          desc: "\u505C\u624B\u7EA6\u4E24\u79D2\u540E\u4E0A\u4F20\u4E00\u6B21\uFF0C\u4E0D\u6253\u65AD\u8F93\u5165",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u6539\u52A8\u540E\u81EA\u52A8\u4E0A\u4F20", checked: cloudAuto, onChange: setCloudAuto })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u540C\u6B65\u540E\u6574\u7406\u8BB0\u5FC6",
          desc: "\u53CC\u7AEF\u5408\u5E76\u540E\u8BA9\u5BF9\u8BDD\u6A21\u578B\u53BB\u6389\u91CD\u590D\u8BB0\u5FC6\uFF1B\u672A\u914D\u7F6E\u6A21\u578B\u65F6\u8DF3\u8FC7",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u540C\u6B65\u540E\u6574\u7406\u8BB0\u5FC6", checked: cloudReconcile, onChange: setCloudReconcile })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sep2, {}),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr auto", gap: "var(--w-space-2)", padding: "13px 0" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(SignalButton, { children: "\u7ACB\u5373\u540C\u6B65" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u6D4B\u8BD5\u8FDE\u63A5" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", children: "\u4EE4\u724C\u53EA\u4FDD\u5B58\u5728\u8FD9\u53F0\u8BBE\u5907\u7684\u8BBE\u7F6E\u91CC\uFF0Csettings.json \u4E0D\u4F1A\u4E0A\u4F20\uFF1B\u4ED3\u5E93\u91CC\u53EA\u6709\u5B58\u6863\u4E0E\u6570\u636E\u6587\u4EF6\uFF0C\u7ACB\u7ED8/\u80CC\u666F\u7B49\u56FE\u7247\u7D20\u6750\u4E0D\u4E0A\u4F20\u3002" })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u81EA\u52A8\u66F4\u65B0", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(
        SettingRow,
        {
          title: "\u6D4B\u8BD5\u8BA1\u5212\uFF08Beta \u6E20\u9053\uFF09",
          desc: "\u5F00\u542F\u540E *-beta \u6D4B\u8BD5\u7248 tag \u4E5F\u4F1A\u63D0\u793A\u66F4\u65B0",
          control: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Sw, { label: "\u6D4B\u8BD5\u8BA1\u5212\uFF08Beta \u6E20\u9053\uFF09", checked: beta, onChange: setBeta })
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr auto", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(SignalButton, { children: "\u68C0\u67E5\u66F4\u65B0" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u4E0B\u8F7D" })
      ] })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u7D20\u6750\u5E93\uFF08\u80CC\u666F \xB7 \u97F3\u4E50\uFF09", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "s-row__desc", style: { textAlign: "center" }, children: "\u5BFC\u5165 / \u7BA1\u7406\u80CC\u666F\u56FE\u4E0E\u97F3\u4E50\uFF0C\u5E76\u628A\u97F3\u4E50\u8BBE\u4E3A\u5DE6\u952E\u97F3\u3001\u53F3\u952E\u97F3\u3001\u6309\u952E\u97F3\u6216\u80CC\u666F\u97F3\u8F6E\u64AD" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(BtnRow, { children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u8FDB\u5165\u7D20\u6750\u5E93" }) })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u80FD\u529B\u5305\uFF08Skills\uFF09", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", style: { textAlign: "center" }, children: "\u8FD8\u6CA1\u6709\u88C5\u80FD\u529B\u5305\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "s-row__desc", style: { textAlign: "center" }, children: "\u4E00\u4E2A\u80FD\u529B\u5305 = \u4E00\u4E2A\u76EE\u5F55\uFF0C\u53EF\u542B SKILL.md\uFF08\u5199\u7ED9 AI \u7684\u8BF4\u660E\u4E66\uFF0C\u6309\u9700\u52A0\u8F7D\uFF09\u548C tool.json\uFF08\u53EF\u6267\u884C\u5165\u53E3\uFF0C\u590D\u7528 Python/Java \u8FD0\u884C\u65F6\uFF09\u3002\u88C5\u4E0A\u540E AI \u4F1A\u5148\u8BFB\u8BF4\u660E\u4E66\u518D\u52A8\u624B\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(BtnRow, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u5BFC\u5165 zip / \u76EE\u5F55" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "\u7BA1\u7406" })
      ] })
    ] })
  ] });
}
function AdvancedSection() {
  const [taps, setTaps] = (0, import_react34.useState)(0);
  return /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(import_jsx_runtime46.Fragment, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u652F\u6301\u4F5C\u8005", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "s-row__desc", style: { textAlign: "center" }, children: "\u5982\u679C\u60A8\u559C\u6B22\u8FD9\u4E2A\u9879\u76EE\uFF0C\u6B22\u8FCE\u8D5E\u52A9\u4F5C\u8005\u652F\u6301 continued development" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--w-space-3)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(SignalButton, { children: "\u7231\u53D1\u7535\u8D5E\u52A9" }),
        /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { children: "B\u7AD9\u4E3B\u9875" })
      ] })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)(SettingCard, { title: "\u5F00\u53D1\u8005\u5C55\u793A\u6A21\u5F0F", children: [
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "s-row__desc", style: { textAlign: "center" }, children: "\u9690\u85CF\u6A21\u5F0F\uFF1A\u4E3A\u521B\u4F5C\u8005\u63D0\u4F9B\u7684\u5C55\u793A\u6848\u7F16\u8F91 / \u64AD\u653E\u529F\u80FD\uFF08\u542B\u591A\u7ACB\u7ED8\u652F\u6301\uFF09" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)("p", { className: "cap", style: { textAlign: "center" }, children: "\u6EDA\u5230\u8BBE\u7F6E\u6700\u5E95\u90E8\u540E\uFF0C\u8FDE\u7EED\u70B9\u6309\u201C\u8FDE\u51FB\u89E3\u9501\u201D\u6309\u94AE 114514 \u6B21\u53EF\u5F00\u542F" }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(BtnRow, { children: /* @__PURE__ */ (0, import_jsx_runtime46.jsx)(Btn, { onClick: () => setTaps(taps + 1), children: "\u8FDE\u51FB\u89E3\u9501" }) }),
      /* @__PURE__ */ (0, import_jsx_runtime46.jsxs)("p", { className: "cap", style: { textAlign: "center" }, children: [
        "\u5DF2\u70B9\u6309 ",
        taps,
        " \u6B21"
      ] })
    ] })
  ] });
}

// src/screens/section/index.tsx
var screens6 = {
  settingssection: SettingsSection
};

// src/screens/settings-ai/aiModels.tsx
var import_react35 = __toESM(require_react(), 1);
var import_jsx_runtime47 = __toESM(require_jsx_runtime(), 1);
var BUILTIN_PRESETS = [
  { name: "\u901A\u7528\u5BF9\u8BDD", hint: "\u5DE5\u5177 + JSON", tools: true, json: true, vision: false, ocr: false, reasoning: false, imageGen: false, audioIn: false, audioOut: false, videoIn: false, videoOut: false },
  { name: "\u89C6\u89C9\u6A21\u578B", hint: "\u8BC6\u56FE + OCR", tools: false, json: false, vision: true, ocr: true, reasoning: false, imageGen: false, audioIn: false, audioOut: false, videoIn: false, videoOut: false },
  { name: "\u5168\u6A21\u6001", hint: "\u5341\u9879\u80FD\u529B\u5168\u5F00", tools: true, json: true, vision: true, ocr: true, reasoning: true, imageGen: true, audioIn: true, audioOut: true, videoIn: true, videoOut: true },
  { name: "\u7EAF\u6587\u672C\u5C0F\u6A21\u578B", hint: "\u80FD\u529B\u5168\u5173\uFF0C\u505A\u4FBF\u5B9C\u515C\u5E95", tools: false, json: false, vision: false, ocr: false, reasoning: false, imageGen: false, audioIn: false, audioOut: false, videoIn: false, videoOut: false }
];
var newModel = (model) => ({
  id: String(Math.random()).slice(2, 10),
  model,
  label: "",
  enabled: true,
  contextWindow: "",
  supportsTools: true,
  supportsJson: false,
  supportsReasoning: false,
  supportsVision: false,
  supportsImageGen: false,
  supportsOcr: false,
  supportsAudioInput: false,
  supportsAudioOutput: false,
  supportsVideoInput: false,
  supportsVideoOutput: false,
  priority: 0,
  showThinkTierBar: true,
  showThinkToggle: true,
  showQuickButtons: true
});
var parseWindow = (text) => {
  const value = Number.parseInt(text.trim(), 10);
  return Number.isFinite(value) && value > 0 ? Math.min(2e6, Math.max(1e3, value)) : 0;
};
var displayNameOf = (row) => row.label.trim() ? row.label.trim() : row.model.trim() ? row.model.trim() : "\uFF08\u672A\u586B\u6A21\u578B\uFF09";
var capabilityTextOf = (row) => {
  const parts = [];
  const window2 = parseWindow(row.contextWindow);
  if (window2) parts.push(`${Math.round(window2 / 1e3 * 10) / 10}K \u4E0A\u4E0B\u6587`);
  if (row.supportsTools) parts.push("\u5DE5\u5177");
  if (row.supportsJson) parts.push("JSON");
  if (row.supportsReasoning) parts.push("\u601D\u8003");
  if (row.supportsVision) parts.push("\u8BC6\u56FE");
  if (row.supportsImageGen) parts.push("\u751F\u56FE");
  if (row.supportsOcr) parts.push("OCR");
  if (row.supportsAudioInput) parts.push("\u542C\u97F3\u9891");
  if (row.supportsAudioOutput) parts.push("\u51FA\u97F3\u9891");
  if (row.supportsVideoInput) parts.push("\u770B\u89C6\u9891");
  if (row.supportsVideoOutput) parts.push("\u51FA\u89C6\u9891");
  if (!row.enabled) parts.push("\u5DF2\u505C\u7528");
  return parts.length === 0 ? "\u672A\u6807\u6CE8\u80FD\u529B" : parts.join(" \xB7 ");
};
var providerTitleOf = (row) => row.name.trim() ? row.name.trim() : "\uFF08\u672A\u547D\u540D\u5382\u5546\uFF09";
var modelCountOf = (row) => `${row.models.filter((model) => model.enabled).length}/${row.models.length} \u6B3E\u542F\u7528`;
var CAPTION = { fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)" };
var BODY_SM = { fontSize: "var(--w-size-small)", color: "var(--w-ink-body)" };
function ToggleLine({
  title: title2,
  checked,
  onChange
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
    /* @__PURE__ */ (0, import_jsx_runtime47.jsx)("span", { style: BODY_SM, children: title2 }),
    /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(GlassSwitch, { label: title2, checked, onChange })
  ] });
}
function AiModels({ navigate, back }) {
  const [providers, setProviders] = (0, import_react35.useState)([]);
  const [customPresets, setCustomPresets] = (0, import_react35.useState)([]);
  const [selectedId, setSelectedId] = (0, import_react35.useState)(null);
  const [showDisplaySwitches, setShowDisplaySwitches] = (0, import_react35.useState)(false);
  const [status, setStatus] = (0, import_react35.useState)("");
  const selected = providers.flatMap((provider) => provider.models).find((model) => model.id === selectedId) ?? null;
  const patchProvider = (id, next) => setProviders((list) => list.map((item) => item.id === id ? { ...item, ...next } : item));
  const patchModel = (id, next) => setProviders(
    (list) => list.map((provider) => ({
      ...provider,
      models: provider.models.map((model) => model.id === id ? { ...model, ...next } : model)
    }))
  );
  const moveProvider = (id, delta) => setProviders((list) => {
    const index = list.findIndex((item) => item.id === id);
    const target = index + delta;
    if (index < 0 || target < 0 || target >= list.length) return list;
    const copy = list.slice();
    const [row] = copy.splice(index, 1);
    copy.splice(target, 0, row);
    return copy;
  });
  const moveModel = (providerId, modelId, delta) => setProviders(
    (list) => list.map((provider) => {
      if (provider.id !== providerId) return provider;
      const index = provider.models.findIndex((model) => model.id === modelId);
      const target = index + delta;
      if (index < 0 || target < 0 || target >= provider.models.length) return provider;
      const models = provider.models.slice();
      const [row] = models.splice(index, 1);
      models.splice(target, 0, row);
      return { ...provider, models };
    })
  );
  const applyPreset = (preset) => {
    if (!selected) return;
    patchModel(selected.id, {
      supportsTools: preset.tools,
      supportsJson: preset.json,
      supportsVision: preset.vision,
      supportsOcr: preset.ocr,
      supportsReasoning: preset.reasoning,
      supportsImageGen: preset.imageGen,
      supportsAudioInput: preset.audioIn,
      supportsAudioOutput: preset.audioOut,
      supportsVideoInput: preset.videoIn,
      supportsVideoOutput: preset.videoOut
    });
    setStatus(`\u5DF2\u5957\u7528\u300C${preset.name}\u300D\uFF1A${preset.hint}`);
  };
  const header = /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
    PageHeader,
    {
      title: "AI \u6A21\u578B\u7BA1\u7406",
      subtitle: "\u6BCF\u6B3E\u6A21\u578B\u5355\u72EC\u914D\u4E0A\u4E0B\u6587\u7A97\u53E3\u4E0E\u80FD\u529B\uFF1B\u9884\u8BBE\u4E00\u952E\u6279\u91CF\u8BBE\u7F6E\uFF0C\u6539\u52A8\u81EA\u52A8\u4FDD\u5B58\u3002",
      onBack: selected ? () => setSelectedId(null) : back,
      backText: selected ? "\u8FD4\u56DE\u5217\u8868" : "\u8FD4\u56DE",
      right: selected ? null : /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(GlassButton, { size: "sm", onClick: () => navigate("/aiProviders"), children: "\u5382\u5546\u7BA1\u7406" })
    }
  );
  if (selected) {
    const patch = (next) => patchModel(selected.id, next);
    return /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)(PageShell, { children: [
      header,
      /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)("div", { className: "screen screen--narrow", children: [
        /* @__PURE__ */ (0, import_jsx_runtime47.jsx)("p", { style: { margin: 0, fontSize: "var(--w-size-subhead)", color: "var(--w-ink-display)" }, children: displayNameOf(selected) }),
        /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(Card, { title: "\u57FA\u7840", children: /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-3)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "88px 1fr", alignItems: "center", gap: "var(--w-space-3)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime47.jsx)("span", { style: BODY_SM, children: "\u6A21\u578B id" }),
            /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(Field, { value: selected.model, onChange: (model) => patch({ model }), placeholder: "\u5982 deepseek-chat", label: "\u6A21\u578B id" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "88px 1fr", alignItems: "center", gap: "var(--w-space-3)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime47.jsx)("span", { style: BODY_SM, children: "\u663E\u793A\u540D" }),
            /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(Field, { value: selected.label, onChange: (label) => patch({ label }), placeholder: "\u7559\u7A7A\u5219\u7528\u6A21\u578B id", label: "\u663E\u793A\u540D" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "88px 1fr", alignItems: "center", gap: "var(--w-space-3)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime47.jsx)("span", { style: BODY_SM, children: "\u4E0A\u4E0B\u6587\u7A97\u53E3" }),
            /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
              Field,
              {
                value: selected.contextWindow,
                onChange: (contextWindow) => patch({ contextWindow }),
                placeholder: "\u5982 128000",
                type: "number",
                label: "\u4E0A\u4E0B\u6587\u7A97\u53E3"
              }
            )
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime47.jsx)("p", { style: { margin: 0, ...CAPTION }, children: "\u7559\u7A7A = \u7528\u5168\u5C40\u4E0A\u9650\uFF1B\u586B\u4E86\u6309 70% \u6298\u53E0\u5386\u53F2\uFF0C\u4E3A\u8F93\u51FA\u7559\u7A7A\u95F4\u3002" })
        ] }) }),
        /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)(Card, { title: "\u80FD\u529B\u9884\u8BBE\uFF08\u4E00\u952E\u6279\u91CF\u8BBE\u7F6E\uFF09", children: [
          /* @__PURE__ */ (0, import_jsx_runtime47.jsx)("div", { style: { display: "flex", flexWrap: "wrap", gap: "var(--w-space-2)" }, children: BUILTIN_PRESETS.map((preset) => /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(GlassButton, { size: "sm", onClick: () => applyPreset(preset), children: preset.name }, preset.name)) }),
          /* @__PURE__ */ (0, import_jsx_runtime47.jsx)("p", { style: { margin: "var(--w-space-2) 0 0", ...CAPTION }, children: "\u901A\u7528\u5BF9\u8BDD = \u5DE5\u5177 + JSON\uFF1B\u89C6\u89C9\u6A21\u578B = \u8BC6\u56FE + OCR\uFF1B\u5168\u6A21\u6001 = \u5168\u5F00\uFF1B\u7EAF\u6587\u672C\u5C0F\u6A21\u578B = \u5168\u5173\u3002" }),
          /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(Sep, { bleed: true }),
          customPresets.length > 0 ? /* @__PURE__ */ (0, import_jsx_runtime47.jsx)("div", { style: { display: "flex", flexWrap: "wrap", alignItems: "center", gap: "var(--w-space-2)" }, children: customPresets.map((preset) => /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)("span", { style: { display: "inline-flex", alignItems: "center", gap: "2px" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(TextBtn, { onClick: () => applyPreset(preset), children: preset.name }),
            /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
              TextBtn,
              {
                onClick: () => {
                  setCustomPresets((list) => list.filter((item) => item.name !== preset.name));
                  setStatus(`\u5DF2\u5220\u9664\u9884\u8BBE\u300C${preset.name}\u300D\u3002`);
                },
                children: "\u5220\u9664"
              }
            )
          ] }, preset.name)) }) : null,
          /* @__PURE__ */ (0, import_jsx_runtime47.jsx)("div", { style: { marginTop: "var(--w-space-2)" }, children: /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
            TextBtn,
            {
              onClick: () => {
                const name = `${displayNameOf(selected)} \xB7 \u9884\u8BBE`;
                setCustomPresets((list) => [
                  ...list,
                  {
                    name,
                    hint: "\u81EA\u5B9A\u4E49",
                    tools: selected.supportsTools,
                    json: selected.supportsJson,
                    vision: selected.supportsVision,
                    ocr: selected.supportsOcr,
                    reasoning: selected.supportsReasoning,
                    imageGen: selected.supportsImageGen,
                    audioIn: selected.supportsAudioInput,
                    audioOut: selected.supportsAudioOutput,
                    videoIn: selected.supportsVideoInput,
                    videoOut: selected.supportsVideoOutput
                  }
                ]);
                setStatus(`\u5DF2\u4FDD\u5B58\u9884\u8BBE\u300C${name}\u300D\u3002`);
              },
              children: "\u53E6\u5B58\u4E3A\u9884\u8BBE"
            }
          ) })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(Card, { title: "\u6587\u672C\u4E0E\u5DE5\u5177", children: /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
            ToggleLine,
            {
              title: "\u5DE5\u5177\uFF08function calling\uFF09",
              checked: selected.supportsTools,
              onChange: (supportsTools) => patch({ supportsTools })
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
            ToggleLine,
            {
              title: "JSON \u7ED3\u6784\u5316\u8F93\u51FA",
              checked: selected.supportsJson,
              onChange: (supportsJson) => patch({ supportsJson })
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
            ToggleLine,
            {
              title: "\u6DF1\u5EA6\u601D\u8003\uFF08\u63A8\u7406\u5F3A\u5EA6\uFF09",
              checked: selected.supportsReasoning,
              onChange: (supportsReasoning) => patch({ supportsReasoning })
            }
          )
        ] }) }),
        /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(Card, { title: "\u56FE\u50CF", children: /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
            ToggleLine,
            {
              title: "\u8BC6\u56FE\uFF08\u770B\u56FE\u7406\u89E3\uFF09",
              checked: selected.supportsVision,
              onChange: (supportsVision) => patch({ supportsVision })
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
            ToggleLine,
            {
              title: "\u751F\u56FE\uFF08\u751F\u6210\u56FE\u7247\uFF09",
              checked: selected.supportsImageGen,
              onChange: (supportsImageGen) => patch({ supportsImageGen })
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
            ToggleLine,
            {
              title: "OCR\uFF08\u81EA\u5E26\u6587\u5B57\u8BC6\u522B\uFF09",
              checked: selected.supportsOcr,
              onChange: (supportsOcr) => patch({ supportsOcr })
            }
          )
        ] }) }),
        /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(Card, { title: "\u97F3\u9891\u4E0E\u89C6\u9891", children: /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
            ToggleLine,
            {
              title: "\u542C\u97F3\u9891\uFF08\u8F93\u5165\u97F3\u9891\uFF09",
              checked: selected.supportsAudioInput,
              onChange: (supportsAudioInput) => patch({ supportsAudioInput })
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
            ToggleLine,
            {
              title: "\u51FA\u97F3\u9891\uFF08\u8BED\u97F3\u5408\u6210\uFF09",
              checked: selected.supportsAudioOutput,
              onChange: (supportsAudioOutput) => patch({ supportsAudioOutput })
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
            ToggleLine,
            {
              title: "\u770B\u89C6\u9891\uFF08\u89C6\u9891\u7406\u89E3\uFF09",
              checked: selected.supportsVideoInput,
              onChange: (supportsVideoInput) => patch({ supportsVideoInput })
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
            ToggleLine,
            {
              title: "\u51FA\u89C6\u9891\uFF08\u89C6\u9891\u751F\u6210\uFF09",
              checked: selected.supportsVideoOutput,
              onChange: (supportsVideoOutput) => patch({ supportsVideoOutput })
            }
          )
        ] }) }),
        /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)(Card, { title: "\u4F7F\u7528\u987A\u5E8F\uFF08Priority\uFF09", children: [
          /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-3)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
              GlassSlider,
              {
                label: "\u4F7F\u7528\u987A\u5E8F",
                value: selected.priority / 20,
                onChange: (value) => patch({ priority: Math.round(value * 20) }),
                format: (value) => String(Math.round(value * 20))
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime47.jsx)("span", { style: { minWidth: "28px", textAlign: "center", color: "var(--w-ink-body)" }, children: selected.priority })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime47.jsx)("p", { style: { margin: "var(--w-space-2) 0 0", ...CAPTION }, children: "\u6570\u503C\u5C0F\u7684\u4F18\u5148\u88AB\u8DEF\u7531\u9009\u4E2D\uFF1B0 = \u6309\u5217\u8868\u987A\u5E8F\u3002\u4E0A\u79FB/\u4E0B\u79FB\u6309\u94AE\u8C03\u6574\u5217\u8868\u987A\u5E8F\u3002" })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)(Card, { children: [
          /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-3)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)("div", { style: { flex: 1, display: "flex", flexDirection: "column", gap: "2px" }, children: [
              /* @__PURE__ */ (0, import_jsx_runtime47.jsx)("span", { style: { fontSize: "var(--w-size-subhead)", color: "var(--w-ink-display)" }, children: "\u5BF9\u8BDD\u9875\u63A7\u4EF6\u663E\u793A" }),
              /* @__PURE__ */ (0, import_jsx_runtime47.jsx)("span", { style: CAPTION, children: "\u6309\u8FD9\u6B3E\u6A21\u578B\u51B3\u5B9A\u5BF9\u8BDD\u9875\u663E\u793A\u54EA\u4E9B\u63A7\u4EF6\u3002" })
            ] }),
            /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
              GlassSwitch,
              {
                label: "\u5BF9\u8BDD\u9875\u63A7\u4EF6\u663E\u793A",
                checked: showDisplaySwitches,
                onChange: setShowDisplaySwitches
              }
            )
          ] }),
          showDisplaySwitches ? /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)", marginTop: "var(--w-space-2)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
              ToggleLine,
              {
                title: "\u601D\u8003\u7B49\u7EA7\u6761",
                checked: selected.showThinkTierBar,
                onChange: (showThinkTierBar) => patch({ showThinkTierBar })
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
              ToggleLine,
              {
                title: "\u601D\u8003\u5F00\u5173",
                checked: selected.showThinkToggle,
                onChange: (showThinkToggle) => patch({ showThinkToggle })
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
              ToggleLine,
              {
                title: "\u5FEB\u901F\u6309\u94AE\u7EC4",
                checked: selected.showQuickButtons,
                onChange: (showQuickButtons) => patch({ showQuickButtons })
              }
            )
          ] }) : null
        ] }),
        status ? /* @__PURE__ */ (0, import_jsx_runtime47.jsx)("p", { style: { margin: 0, ...CAPTION }, children: status }) : null
      ] })
    ] });
  }
  return /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)(PageShell, { children: [
    header,
    /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)("div", { className: "screen screen--narrow", children: [
      providers.map((provider) => /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)(Card, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)("div", { style: { display: "flex", alignItems: "flex-start", gap: "var(--w-space-3)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)("div", { style: { flex: 1, minWidth: 0, display: "flex", flexDirection: "column", gap: "2px" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
              "span",
              {
                style: {
                  fontSize: "var(--w-size-body)",
                  fontWeight: 700,
                  color: "var(--w-ink-display)",
                  overflow: "hidden",
                  textOverflow: "ellipsis",
                  whiteSpace: "nowrap"
                },
                children: providerTitleOf(provider)
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime47.jsx)("span", { style: { ...CAPTION, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }, children: provider.url }),
            /* @__PURE__ */ (0, import_jsx_runtime47.jsx)("span", { style: CAPTION, children: modelCountOf(provider) })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
            GlassSwitch,
            {
              label: `${providerTitleOf(provider)} \u542F\u7528`,
              checked: provider.enabled,
              onChange: (enabled) => patchProvider(provider.id, { enabled })
            }
          )
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime47.jsx)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-3)", marginTop: "var(--w-space-3)" }, children: provider.models.map((model) => /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)(Card, { tight: true, children: [
          /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)("div", { style: { display: "flex", alignItems: "flex-start", gap: "var(--w-space-3)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)("div", { style: { flex: 1, minWidth: 0, display: "flex", flexDirection: "column", gap: "2px" }, children: [
              /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
                "span",
                {
                  style: {
                    fontSize: "var(--w-size-small)",
                    fontWeight: 700,
                    color: "var(--w-ink-display)",
                    overflow: "hidden",
                    textOverflow: "ellipsis",
                    whiteSpace: "nowrap"
                  },
                  children: displayNameOf(model)
                }
              ),
              /* @__PURE__ */ (0, import_jsx_runtime47.jsx)("span", { style: { ...CAPTION, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }, children: capabilityTextOf(model) })
            ] }),
            /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
              GlassSwitch,
              {
                label: `${displayNameOf(model)} \u542F\u7528`,
                checked: model.enabled,
                onChange: (enabled) => patchModel(model.id, { enabled })
              }
            )
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)("div", { style: { display: "flex", flexWrap: "wrap", alignItems: "center", gap: "var(--w-space-2)", marginTop: "var(--w-space-2)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(TextBtn, { onClick: () => setSelectedId(model.id), children: "\u7F16\u8F91" }),
            /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
              TextBtn,
              {
                onClick: () => {
                  moveModel(provider.id, model.id, -1);
                  setStatus("\u987A\u5E8F\u5DF2\u4FDD\u5B58\uFF1A\u540C\u80FD\u529B\u4E0B\u6392\u5728\u524D\u9762\u7684\u6A21\u578B\u4F18\u5148\u88AB\u9009\u4E2D\u3002");
                },
                children: "\u4E0A\u79FB"
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
              TextBtn,
              {
                onClick: () => {
                  moveModel(provider.id, model.id, 1);
                  setStatus("\u987A\u5E8F\u5DF2\u4FDD\u5B58\uFF1A\u540C\u80FD\u529B\u4E0B\u6392\u5728\u524D\u9762\u7684\u6A21\u578B\u4F18\u5148\u88AB\u9009\u4E2D\u3002");
                },
                children: "\u4E0B\u79FB"
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
              TextBtn,
              {
                onClick: () => {
                  if (!window.confirm(`\u5220\u9664\u6A21\u578B
\u5220\u6389\u300C${displayNameOf(model)}\u300D\uFF1F`)) return;
                  setProviders(
                    (list) => list.map(
                      (item) => item.id === provider.id ? { ...item, models: item.models.filter((row) => row.id !== model.id) } : item
                    )
                  );
                  setStatus("\u5DF2\u5220\u9664\u6A21\u578B\u3002");
                },
                children: "\u5220\u9664"
              }
            )
          ] })
        ] }, model.id)) }),
        /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)("div", { style: { display: "flex", flexWrap: "wrap", alignItems: "center", gap: "var(--w-space-2)", marginTop: "var(--w-space-3)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
            GlassButton,
            {
              size: "sm",
              onClick: () => {
                const row = newModel("new-model");
                setProviders(
                  (list) => list.map(
                    (item) => item.id === provider.id ? { ...item, models: [...item.models, row] } : item
                  )
                );
                setStatus("\u5DF2\u52A0\u6A21\u578B\uFF0C\u8FDB\u5165\u8BE6\u60C5\u586B\u5199\u6A21\u578B id \u4E0E\u80FD\u529B\u3002");
                setSelectedId(row.id);
              },
              children: "\u52A0\u6A21\u578B"
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
            TextBtn,
            {
              onClick: () => {
                moveProvider(provider.id, -1);
                setStatus("\u987A\u5E8F\u5DF2\u4FDD\u5B58\uFF1A\u6392\u5728\u524D\u9762\u7684\u5382\u5546\u4F18\u5148\u88AB\u8F6E\u6362\u3002");
              },
              children: "\u4E0A\u79FB"
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
            TextBtn,
            {
              onClick: () => {
                moveProvider(provider.id, 1);
                setStatus("\u987A\u5E8F\u5DF2\u4FDD\u5B58\uFF1A\u6392\u5728\u524D\u9762\u7684\u5382\u5546\u4F18\u5148\u88AB\u8F6E\u6362\u3002");
              },
              children: "\u4E0B\u79FB"
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
            TextBtn,
            {
              onClick: () => {
                if (!window.confirm(`\u5220\u9664\u5382\u5546
\u5220\u6389\u300C${providerTitleOf(provider)}\u300D\u53CA\u5176\u4E0B\u6240\u6709\u6A21\u578B\uFF1F`)) return;
                setProviders((list) => list.filter((item) => item.id !== provider.id));
                setStatus("\u5DF2\u5220\u9664\u5382\u5546\u3002");
              },
              children: "\u5220\u9664\u5382\u5546"
            }
          )
        ] })
      ] }, provider.id)),
      providers.length === 0 ? /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
        Empty,
        {
          title: "\u8FD8\u6CA1\u6709\u5382\u5546",
          reason: "\u6A21\u578B\u90FD\u6302\u5728\u5382\u5546\u4E0B\u9762\uFF0C\u5382\u5546\u5217\u8868\u662F\u7A7A\u7684\uFF0C\u6240\u4EE5\u8FD9\u91CC\u4E5F\u6CA1\u6709\u6A21\u578B\u53EF\u914D\u3002",
          hint: "\u5148\u70B9\u300C\u52A0\u5382\u5546\u300D\uFF0C\u6216\u5230\u300C\u5382\u5546\u7BA1\u7406\u300D\u8865\u7AEF\u70B9\u4E0E\u5BC6\u94A5\u3002"
        }
      ) : null,
      /* @__PURE__ */ (0, import_jsx_runtime47.jsxs)("div", { style: { display: "flex", flexDirection: "column", alignItems: "flex-start", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(
          GlassButton,
          {
            size: "sm",
            onClick: () => {
              setProviders((list) => [
                ...list,
                { id: String(Math.random()).slice(2, 10), name: "\u65B0\u5382\u5546", url: "", enabled: true, models: [] }
              ]);
              setStatus("\u5DF2\u52A0\u5382\u5546\uFF0C\u53BB\u300C\u5382\u5546\u7BA1\u7406\u300D\u8865\u7AEF\u70B9\u4E0E\u5BC6\u94A5\u3002");
            },
            children: "\u52A0\u5382\u5546"
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime47.jsx)(TextBtn, { onClick: () => navigate("/modelDeployment"), children: "\u6A21\u578B\u90E8\u7F72\u5411\u5BFC" }),
        /* @__PURE__ */ (0, import_jsx_runtime47.jsx)("p", { style: { margin: 0, ...CAPTION }, children: "\u7AEF\u70B9\u4E0E\u5BC6\u94A5\u5728\u300C\u5382\u5546\u7BA1\u7406\u300D\u7EF4\u62A4\uFF1B\u8FD9\u91CC\u53EA\u7BA1\u6A21\u578B\u4E0E\u80FD\u529B\u3002" })
      ] }),
      status ? /* @__PURE__ */ (0, import_jsx_runtime47.jsx)("p", { style: { margin: 0, ...CAPTION }, children: status }) : null
    ] })
  ] });
}

// src/screens/settings-ai/aiProviders.tsx
var import_react36 = __toESM(require_react(), 1);
var import_jsx_runtime48 = __toESM(require_jsx_runtime(), 1);
var splitKeys = (text) => text.split(/[\n\r,，;；]/).map((part) => part.trim()).filter((part) => part.length > 0);
var titleOf = (provider) => provider.name.trim() ? provider.name : "\uFF08\u672A\u547D\u540D\u5382\u5546\uFF09";
var parseTimeout = (text) => {
  const value = Number.parseInt(text.trim(), 10);
  return Number.isFinite(value) && value > 0 ? Math.min(600, Math.max(3, value)) : null;
};
var summaryOf = (provider) => {
  const on = provider.models.filter((model) => model.enabled).length;
  const timeout = parseTimeout(provider.timeout);
  return `${on}/${provider.models.length} \u6B3E\u6A21\u578B \xB7 ${provider.keys.length} \u679A\u5BC6\u94A5${timeout ? ` \xB7 \u8D85\u65F6 ${timeout}s` : ""}`;
};
var modelsHintOf = (provider) => provider.models.length === 0 ? "\u8FD8\u6CA1\u6709\u6A21\u578B\uFF1A\u5230\u300C\u6A21\u578B\u7BA1\u7406\u300D\u91CC\u52A0\u4E00\u6B3E\u3002" : "\u6A21\u578B\uFF1A" + provider.models.map((model) => (model.label.trim() ? model.label : model.model) + (model.enabled ? "" : "\uFF08\u505C\u7528\uFF09")).join("\u3001");
var rowAction = { marginRight: "var(--w-space-2)" };
function AiProviders({ navigate, back }) {
  const [providers, setProviders] = (0, import_react36.useState)([]);
  const [newName, setNewName] = (0, import_react36.useState)("");
  const [newModel2, setNewModel] = (0, import_react36.useState)("");
  const [newUrl, setNewUrl] = (0, import_react36.useState)("");
  const [newKeys, setNewKeys] = (0, import_react36.useState)("");
  const [newTimeout, setNewTimeout] = (0, import_react36.useState)("");
  const [status, setStatus] = (0, import_react36.useState)("");
  const patch = (id, next) => setProviders((list) => list.map((item) => item.id === id ? { ...item, ...next } : item));
  const move = (id, delta) => setProviders((list) => {
    const index = list.findIndex((item) => item.id === id);
    const target = index + delta;
    if (index < 0 || target < 0 || target >= list.length) return list;
    const copy = list.slice();
    const [row] = copy.splice(index, 1);
    copy.splice(target, 0, row);
    return copy;
  });
  const editBasics = (provider) => {
    const name = window.prompt("\u5382\u5546\u540D\u79F0\n\u597D\u8BA4\u7684\u540D\u5B57\uFF0C\u5982\u300C\u7845\u57FA\u6D41\u52A8\u300D\u300CDeepSeek \u5B98\u65B9\u300D\u3002", provider.name);
    if (name === null) return;
    const url = window.prompt("\u7AEF\u70B9\u5730\u5740\nOpenAI \u517C\u5BB9\u7684 chat/completions \u5730\u5740\u3002", provider.url);
    if (url === null) return;
    patch(provider.id, { name: name.trim(), url: url.trim() });
    setStatus("\u5382\u5546\u4FE1\u606F\u5DF2\u4FDD\u5B58\u3002");
  };
  const editKeys = (provider) => {
    const keys = window.prompt(
      `\u300C${titleOf(provider)}\u300D\u7684\u5BC6\u94A5
\u4E00\u884C\u4E00\u679A\uFF0C\u53EF\u591A\u679A\u8F6E\u6D41\u7528\uFF08\u4E5F\u53EF\u7528\u9017\u53F7\u5206\u9694\uFF09\u3002`,
      provider.keys.join("\n")
    );
    if (keys === null) return;
    patch(provider.id, { keys: splitKeys(keys) });
    setStatus("\u5BC6\u94A5\u5DF2\u4FDD\u5B58\u3002");
  };
  const editTimeout = (provider) => {
    const timeout = window.prompt(
      `\u300C${titleOf(provider)}\u300D\u7684\u8D85\u65F6
\u79D2\u6570\uFF083~600\uFF09\uFF0C\u7559\u7A7A = \u7528\u5168\u5C40\u8D85\u65F6\u3002`,
      provider.timeout
    );
    if (timeout === null) return;
    patch(provider.id, { timeout: timeout.trim() });
    setStatus("\u8D85\u65F6\u5DF2\u4FDD\u5B58\u3002");
  };
  const remove = (provider) => {
    if (!window.confirm(`\u5220\u9664\u5382\u5546
\u5220\u6389\u300C${titleOf(provider)}\u300D\uFF1F\u5B83\u4E0B\u9762\u7684\u6A21\u578B\u4E5F\u4E00\u8D77\u6CA1\u4E86\u3002`)) return;
    setProviders((list) => list.filter((item) => item.id !== provider.id));
    setStatus("\u5DF2\u5220\u9664\u3002");
  };
  const addProvider = () => {
    const url = newUrl.trim();
    if (url.length === 0) {
      setStatus("\u5148\u586B\u7AEF\u70B9\u5730\u5740\uFF08OpenAI \u517C\u5BB9\uFF09\uFF0C\u518D\u70B9\u300C\u6DFB\u52A0\u5382\u5546\u300D\u3002");
      return;
    }
    const model = newModel2.trim();
    const row = {
      id: String(Math.random()).slice(2, 10),
      name: newName.trim(),
      url,
      keys: splitKeys(newKeys),
      enabled: true,
      timeout: newTimeout.trim(),
      models: model.length > 0 ? [{ model, label: "", enabled: true }] : []
    };
    setProviders((list) => [...list, row]);
    setNewName("");
    setNewUrl("");
    setNewKeys("");
    setNewModel("");
    setNewTimeout("");
    setStatus("\u5DF2\u6DFB\u52A0\u5382\u5546\u3002\u5230\u300C\u6A21\u578B\u7BA1\u7406\u300D\u91CC\u8865\u4E0A\u4E0B\u6587\u7A97\u53E3\u548C\u80FD\u529B\u3002");
  };
  return /* @__PURE__ */ (0, import_jsx_runtime48.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime48.jsx)(
      PageHeader,
      {
        title: "AI \u5382\u5546\u7BA1\u7406",
        subtitle: "\u4E00\u5BB6\u5382\u5546 = \u4E00\u4E2A\u7AEF\u70B9 + \u4E00\u7EC4\u5BC6\u94A5 + \u591A\u6B3E\u6A21\u578B\uFF1B\u987A\u5E8F\u5373\u4F18\u5148\u7EA7\uFF0C\u5931\u7075\u81EA\u52A8\u6362\u4E0B\u4E00\u5BB6\u3002",
        onBack: back,
        backText: "\u2190",
        right: /* @__PURE__ */ (0, import_jsx_runtime48.jsx)(GlassButton, { size: "sm", onClick: () => navigate("/aiModels"), children: "\u6A21\u578B\u7BA1\u7406 \u2192" })
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime48.jsxs)("div", { className: "screen", children: [
      providers.map((provider) => /* @__PURE__ */ (0, import_jsx_runtime48.jsxs)(Card, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime48.jsxs)("div", { style: { display: "flex", alignItems: "flex-start", gap: "var(--w-space-3)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime48.jsxs)("div", { style: { flex: 1, minWidth: 0, display: "flex", flexDirection: "column", gap: "2px" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime48.jsx)(
              "span",
              {
                style: {
                  fontSize: "var(--w-size-body)",
                  fontWeight: 700,
                  color: "var(--w-ink-display)",
                  overflow: "hidden",
                  textOverflow: "ellipsis",
                  whiteSpace: "nowrap"
                },
                children: titleOf(provider)
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime48.jsx)(
              "span",
              {
                style: {
                  fontSize: "var(--w-size-caption)",
                  color: "var(--w-ink-muted)",
                  overflow: "hidden",
                  textOverflow: "ellipsis",
                  whiteSpace: "nowrap"
                },
                children: provider.url
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime48.jsx)("span", { style: { fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)" }, children: summaryOf(provider) })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime48.jsx)(
            GlassSwitch,
            {
              label: `${titleOf(provider)} \u542F\u7528`,
              checked: provider.enabled,
              onChange: (enabled) => {
                patch(provider.id, { enabled });
                setStatus(enabled ? "\u5DF2\u542F\u7528\u8FD9\u5BB6\u5382\u5546\u3002" : "\u5DF2\u505C\u7528\u8FD9\u5BB6\u5382\u5546\uFF1A\u8F6E\u6362\u6C60\u91CC\u4E0D\u518D\u7528\u5B83\u3002");
              }
            }
          )
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime48.jsxs)("div", { style: { display: "flex", flexWrap: "wrap", alignItems: "center", marginTop: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime48.jsx)("span", { style: rowAction, children: /* @__PURE__ */ (0, import_jsx_runtime48.jsx)(TextBtn, { onClick: () => editBasics(provider), children: "\u540D\u79F0/\u7AEF\u70B9" }) }),
          /* @__PURE__ */ (0, import_jsx_runtime48.jsx)("span", { style: rowAction, children: /* @__PURE__ */ (0, import_jsx_runtime48.jsx)(TextBtn, { onClick: () => editKeys(provider), children: "\u5BC6\u94A5" }) }),
          /* @__PURE__ */ (0, import_jsx_runtime48.jsx)("span", { style: rowAction, children: /* @__PURE__ */ (0, import_jsx_runtime48.jsx)(TextBtn, { onClick: () => editTimeout(provider), children: "\u8D85\u65F6" }) }),
          /* @__PURE__ */ (0, import_jsx_runtime48.jsx)("span", { style: rowAction, children: /* @__PURE__ */ (0, import_jsx_runtime48.jsx)(
            TextBtn,
            {
              onClick: () => {
                move(provider.id, -1);
                setStatus("\u987A\u5E8F\u5DF2\u8C03\u6574\uFF1A\u9760\u524D\u7684\u5148\u7528\u3002");
              },
              children: "\u4E0A\u79FB"
            }
          ) }),
          /* @__PURE__ */ (0, import_jsx_runtime48.jsx)("span", { style: rowAction, children: /* @__PURE__ */ (0, import_jsx_runtime48.jsx)(
            TextBtn,
            {
              onClick: () => {
                move(provider.id, 1);
                setStatus("\u987A\u5E8F\u5DF2\u8C03\u6574\u3002");
              },
              children: "\u4E0B\u79FB"
            }
          ) }),
          /* @__PURE__ */ (0, import_jsx_runtime48.jsx)(TextBtn, { onClick: () => remove(provider), children: "\u5220\u9664" })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime48.jsx)(
          "p",
          {
            style: {
              margin: "var(--w-space-2) 0 0",
              fontSize: "var(--w-size-caption)",
              color: "var(--w-ink-muted)"
            },
            children: modelsHintOf(provider)
          }
        )
      ] }, provider.id)),
      providers.length === 0 ? /* @__PURE__ */ (0, import_jsx_runtime48.jsx)(
        Empty,
        {
          title: "\u8FD8\u6CA1\u6709\u5382\u5546",
          reason: "\u8F6E\u6362\u6C60\u91CC\u4E00\u5BB6\u90FD\u6CA1\u6709\uFF1A\u5BF9\u8BDD\u7AEF\u70B9\u8FD8\u6CA1\u63A5\u4E0A\u3002",
          hint: "\u5728\u4E0B\u9762\u300C\u65B0\u589E\u5382\u5546\u300D\u91CC\u586B\u7AEF\u70B9\u3001\u5BC6\u94A5\u548C\u9996\u6B3E\u6A21\u578B\uFF0C\u70B9\u300C\uFF0B \u6DFB\u52A0\u5382\u5546\u300D\u5C31\u4F1A\u51FA\u73B0\u5728\u4E0A\u9762\u3002"
        }
      ) : null,
      /* @__PURE__ */ (0, import_jsx_runtime48.jsx)(Card, { title: "\u65B0\u589E\u5382\u5546", children: /* @__PURE__ */ (0, import_jsx_runtime48.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-3)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime48.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--w-space-3)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime48.jsx)(Field, { value: newName, onChange: setNewName, placeholder: "\u5382\u5546\u540D\uFF08\u5982\uFF1A\u7845\u57FA\u6D41\u52A8\uFF09", label: "\u5382\u5546\u540D" }),
          /* @__PURE__ */ (0, import_jsx_runtime48.jsx)(
            Field,
            {
              value: newModel2,
              onChange: setNewModel,
              placeholder: "\u9996\u6B3E\u6A21\u578B\uFF08\u5982\uFF1Adeepseek-chat\uFF09",
              label: "\u9996\u6B3E\u6A21\u578B"
            }
          )
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime48.jsx)(
          Field,
          {
            value: newUrl,
            onChange: setNewUrl,
            placeholder: "\u7AEF\u70B9\uFF08https://\u2026/v1/chat/completions\uFF09",
            label: "\u7AEF\u70B9"
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime48.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 110px", gap: "var(--w-space-3)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime48.jsx)(
            Field,
            {
              value: newKeys,
              onChange: setNewKeys,
              placeholder: "\u5BC6\u94A5\uFF08\u4E00\u884C\u4E00\u679A\uFF0C\u53EF\u591A\u679A\u8F6E\u6D41\u7528\uFF09",
              label: "\u5BC6\u94A5"
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime48.jsx)(Field, { value: newTimeout, onChange: setNewTimeout, placeholder: "\u8D85\u65F6\u79D2", type: "number", label: "\u8D85\u65F6\u79D2" })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime48.jsx)("div", { children: /* @__PURE__ */ (0, import_jsx_runtime48.jsx)(GlassButton, { size: "md", onClick: addProvider, children: "\uFF0B \u6DFB\u52A0\u5382\u5546" }) })
      ] }) }),
      status ? /* @__PURE__ */ (0, import_jsx_runtime48.jsx)("p", { style: { margin: 0, fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)" }, children: status }) : null
    ] })
  ] });
}

// src/screens/settings-ai/modelDeployment.tsx
var import_react37 = __toESM(require_react(), 1);
var import_jsx_runtime49 = __toESM(require_jsx_runtime(), 1);
var KIND_CHOICES = [
  { value: "Embedding", label: "Embedding" },
  { value: "SmallModel", label: "SmallModel" },
  { value: "LLM", label: "LLM" },
  { value: "Vision", label: "Vision" }
];
var CAPTION2 = { fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)" };
var LABEL = { fontSize: "var(--w-size-caption)", color: "var(--w-ink-label)" };
function buildSuggestions() {
  const cores = Math.max(1, navigator.hardwareConcurrency || 1);
  const ramGb = navigator.deviceMemory ?? 0;
  const tier = cores >= 8 && ramGb >= 16 ? "\u9AD8" : cores >= 4 ? "\u4E2D" : "\u4F4E";
  return [
    {
      kind: "Embedding",
      name: "\u4F60\u5B9A\u7684\u5411\u91CF\u6A21\u578B\u540D",
      exampleSource: "https://\u4F60\u7684\u955C\u50CF/\u6A21\u578B.gguf \u6216 .onnx",
      note: `\u8FD1\u4E49\u68C0\u7D22\u7528\uFF1B${tier}\u914D\u673A\u5EFA\u8BAE\u5148\u8BD5\u5C0F\u5C3A\u5BF8\uFF08\u7EA6 100MB \u7EA7\uFF09`
    },
    {
      kind: "SmallModel",
      name: "\u4F60\u5B9A\u7684\u5C0F\u6A21\u578B\u540D",
      exampleSource: "https://\u4F60\u7684\u955C\u50CF/\u5C0F\u6A21\u578B.gguf",
      note: "\u60C5\u7EEA\u8868\u6F14 / \u5DE5\u5177\u89E3\u6790 / \u72B6\u6001\u5305\u538B\u7F29\uFF1B\u4FBF\u5B9C\u4F18\u5148"
    },
    {
      kind: "LLM",
      name: "\u4F60\u5B9A\u7684\u5927\u6A21\u578B\u540D",
      exampleSource: "https://\u4F60\u7684\u955C\u50CF/\u5927\u6A21\u578B",
      note: `\u4E3B\u5BF9\u8BDD\uFF1B\u5F53\u524D\u7EA6 ${Math.round(ramGb)}GB \u5185\u5B58\u3001${cores} \u6838`
    }
  ];
}
function ModelDeployment({ back }) {
  const [name, setName] = (0, import_react37.useState)("");
  const [kind, setKind] = (0, import_react37.useState)("Embedding");
  const [source, setSource] = (0, import_react37.useState)("");
  const [installPath, setInstallPath] = (0, import_react37.useState)("");
  const [checksum, setChecksum] = (0, import_react37.useState)("");
  const [status, setStatus] = (0, import_react37.useState)("");
  const [suggestions, setSuggestions] = (0, import_react37.useState)([]);
  const deploy = () => {
    if (name.trim().length === 0 || source.trim().length === 0 || installPath.trim().length === 0) {
      setStatus("\u540D\u5B57\u3001\u6765\u6E90\u3001\u5B89\u88C5\u4F4D\u7F6E\u4E09\u9879\u90FD\u8981\u586B\u3002");
      return;
    }
    setStatus("\u90E8\u7F72\u5F02\u5E38\uFF1A\u8FD9\u4E00\u5C42\u6CA1\u6709\u63A5\u4E0A\u4E0B\u8F7D\u4E0E\u89E3\u538B\u670D\u52A1\uFF0C\u8868\u5355\u53EA\u8D1F\u8D23\u6536\u4E0B\u4F60\u8981\u88C5\u7684\u4E1C\u897F\u3002");
  };
  return /* @__PURE__ */ (0, import_jsx_runtime49.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime49.jsx)(
      PageHeader,
      {
        title: "\u6A21\u578B\u90E8\u7F72\u5411\u5BFC",
        subtitle: "\u4E0B\u8F7D / \u6821\u9A8C / \u89E3\u538B / \u5B89\u88C5\u5168\u81EA\u52A8\u3002\u6A21\u578B\u540D\u3001\u6765\u6E90\uFF08\u94FE\u63A5\u6216\u672C\u673A\u6587\u4EF6\uFF09\u3001\u5B89\u88C5\u4F4D\u7F6E\u5168\u90E8\u7531\u4F60\u5B9A\u3002",
        onBack: back,
        backText: "\u2190"
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime49.jsxs)("div", { className: "screen", children: [
      /* @__PURE__ */ (0, import_jsx_runtime49.jsx)(Card, { title: "\u6DFB\u52A0\u90E8\u7F72", children: /* @__PURE__ */ (0, import_jsx_runtime49.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-3)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime49.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "auto 1fr", alignItems: "center", gap: "var(--w-space-3)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime49.jsx)("span", { style: LABEL, children: "\u6A21\u578B\u540D" }),
          /* @__PURE__ */ (0, import_jsx_runtime49.jsx)(Field, { value: name, onChange: setName, placeholder: "\u4F60\u8D77\u7684\u540D\u5B57\uFF0C\u968F\u4FBF\u4E00\u4E2A\u4E5F\u884C", label: "\u6A21\u578B\u540D" })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime49.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "auto 1fr", alignItems: "center", gap: "var(--w-space-3)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime49.jsx)("span", { style: LABEL, children: "\u7C7B\u578B" }),
          /* @__PURE__ */ (0, import_jsx_runtime49.jsx)("span", { children: /* @__PURE__ */ (0, import_jsx_runtime49.jsx)(Select, { label: "\u7C7B\u578B", value: kind, options: KIND_CHOICES, onChange: setKind }) })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime49.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "auto 1fr", alignItems: "center", gap: "var(--w-space-3)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime49.jsx)("span", { style: LABEL, children: "\u6765\u6E90" }),
          /* @__PURE__ */ (0, import_jsx_runtime49.jsx)(
            Field,
            {
              value: source,
              onChange: setSource,
              placeholder: "http(s) \u94FE\u63A5\uFF0C\u6216\u672C\u673A\u6587\u4EF6\u8DEF\u5F84",
              label: "\u6765\u6E90"
            }
          )
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime49.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "auto 1fr", alignItems: "center", gap: "var(--w-space-3)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime49.jsx)("span", { style: LABEL, children: "\u5B89\u88C5\u4F4D\u7F6E" }),
          /* @__PURE__ */ (0, import_jsx_runtime49.jsx)(Field, { value: installPath, onChange: setInstallPath, label: "\u5B89\u88C5\u4F4D\u7F6E" })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime49.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "auto 1fr", alignItems: "center", gap: "var(--w-space-3)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime49.jsx)("span", { style: LABEL, children: "\u6821\u9A8C\u548C" }),
          /* @__PURE__ */ (0, import_jsx_runtime49.jsx)(
            Field,
            {
              value: checksum,
              onChange: setChecksum,
              placeholder: "\u53EF\u9009 SHA256\uFF0C\u586B\u4E86\u624D\u6821\u9A8C",
              label: "\u6821\u9A8C\u548C"
            }
          )
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime49.jsxs)("div", { style: { display: "flex", flexWrap: "wrap", alignItems: "center", gap: "var(--w-space-3)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime49.jsx)(GlassButton, { size: "md", onClick: () => setSuggestions(buildSuggestions()), children: "\u6309\u7535\u8111\u6027\u80FD\u63A8\u8350" }),
          /* @__PURE__ */ (0, import_jsx_runtime49.jsx)(GlassButton, { size: "md", onClick: deploy, children: "\u5F00\u59CB\u90E8\u7F72" }),
          /* @__PURE__ */ (0, import_jsx_runtime49.jsx)(TextBtn, { disabled: true, children: "\u53D6\u6D88" })
        ] }),
        status ? /* @__PURE__ */ (0, import_jsx_runtime49.jsx)("p", { style: { margin: 0, ...CAPTION2 }, children: status }) : null,
        /* @__PURE__ */ (0, import_jsx_runtime49.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime49.jsx)("span", { style: CAPTION2, children: "\u6309\u6027\u80FD\u63A8\u8350\uFF08\u793A\u4F8B\uFF0C\u53EF\u6539\uFF09" }),
          suggestions.map((suggestion) => /* @__PURE__ */ (0, import_jsx_runtime49.jsx)(Card, { tight: true, children: /* @__PURE__ */ (0, import_jsx_runtime49.jsxs)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-3)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime49.jsxs)("div", { style: { flex: 1, minWidth: 0, display: "flex", flexDirection: "column", gap: "2px" }, children: [
              /* @__PURE__ */ (0, import_jsx_runtime49.jsx)("span", { style: { fontSize: "var(--w-size-small)", color: "var(--w-ink-display)" }, children: suggestion.name }),
              /* @__PURE__ */ (0, import_jsx_runtime49.jsx)("span", { style: CAPTION2, children: suggestion.note }),
              /* @__PURE__ */ (0, import_jsx_runtime49.jsx)("span", { style: { ...CAPTION2, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }, children: suggestion.exampleSource })
            ] }),
            /* @__PURE__ */ (0, import_jsx_runtime49.jsx)(
              TextBtn,
              {
                onClick: () => {
                  setName(suggestion.name);
                  setKind(suggestion.kind);
                  setSource(suggestion.exampleSource);
                  setStatus("\u5DF2\u6309\u5EFA\u8BAE\u9884\u586B\uFF08\u540D\u5B57 / \u6765\u6E90 / \u4F4D\u7F6E\u4F60\u90FD\u80FD\u6539\uFF09\u3002");
                },
                children: "\u7528\u8FD9\u6761"
              }
            )
          ] }) }, suggestion.name))
        ] })
      ] }) }),
      /* @__PURE__ */ (0, import_jsx_runtime49.jsx)("h2", { style: { margin: 0, fontSize: "var(--w-size-body)", fontWeight: 700, color: "var(--w-ink-display)" }, children: "\u5DF2\u90E8\u7F72" }),
      /* @__PURE__ */ (0, import_jsx_runtime49.jsx)(
        Empty,
        {
          title: "\u8FD8\u6CA1\u6709\u90E8\u7F72\u767B\u8BB0",
          reason: "\u767B\u8BB0\u8868\u662F\u7A7A\u7684\uFF1A\u8FD9\u53F0\u673A\u5668\u4E0A\u8FD8\u6CA1\u6709\u88C5\u8FC7\u4EFB\u4F55\u672C\u5730\u6A21\u578B\u3002",
          hint: "\u586B\u597D\u4E0A\u9762\u7684\u8868\u5355\u70B9\u300C\u5F00\u59CB\u90E8\u7F72\u300D\uFF0C\u88C5\u5B8C\u7684\u6A21\u578B\u4F1A\u767B\u8BB0\u5230\u8FD9\u91CC\uFF0C\u9644\u72B6\u6001\u4E0E\u5B89\u88C5\u4F4D\u7F6E\u3002"
        }
      )
    ] })
  ] });
}

// src/screens/settings-ai/pluginCenter.tsx
var import_react38 = __toESM(require_react(), 1);
var import_jsx_runtime50 = __toESM(require_jsx_runtime(), 1);
var PLUGINS_DIR_LABEL = "<\u5E94\u7528\u6570\u636E\u76EE\u5F55>\\plugins";
var CAPTION3 = { fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)" };
function PluginCenter({ back }) {
  const [workshopPrompt, setWorkshopPrompt] = (0, import_react38.useState)("");
  const [mcpPrompt, setMcpPrompt] = (0, import_react38.useState)("");
  const [status, setStatus] = (0, import_react38.useState)("\u8FD8\u6CA1\u6709\u63D2\u4EF6\u3002");
  const hostOnly = () => setStatus("\u88C5\u5305\u4E0E\u5DE5\u574A\u7531\u5BBF\u4E3B\u5904\u7406\uFF1A\u8FD9\u4E00\u5C42\u6CA1\u6709\u63A5\u4E0A\u63D2\u4EF6\u670D\u52A1\u3002");
  return /* @__PURE__ */ (0, import_jsx_runtime50.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime50.jsx)(
      PageHeader,
      {
        title: "\u63D2\u4EF6\u4E2D\u5FC3",
        onBack: back,
        backText: "< \u8FD4\u56DE",
        right: /* @__PURE__ */ (0, import_jsx_runtime50.jsx)(GlassButton, { size: "sm", onClick: () => setStatus("\u8FD8\u6CA1\u6709\u63D2\u4EF6\u3002"), children: "\u5237\u65B0" })
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime50.jsxs)("div", { className: "screen", style: { maxWidth: "1000px" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime50.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime50.jsx)("span", { style: { fontSize: "var(--w-size-caption)", color: "var(--w-signal)" }, children: status }),
        /* @__PURE__ */ (0, import_jsx_runtime50.jsx)("span", { style: { ...CAPTION3, lineHeight: 1.6 }, children: "\u63D2\u4EF6\u53EF\u4EE5\u6539\u4EBA\u8BBE\u3001\u6539\u5199\u53D1\u9001\u524D/\u56DE\u590D\u540E\u7684\u6587\u672C\u3001\u8BFB\u53D6\u4E0A\u4E0B\u6587\u3001\u6309\u8F6E\u6B21\u8BB0\u8D26\uFF0C\u4E5F\u80FD\u5E26\u81EA\u5DF1\u7684\u8BBE\u7F6E\u9875\u4E0E\u9875\u9762\u3002\u94A9\u5B50\u8FD0\u884C\u5728\u53D7\u9650 JS \u6C99\u7BB1\u91CC\uFF08\u8BED\u53E5\u6570\u4E0E\u5185\u5B58\u6709\u786C\u4E0A\u9650\uFF0C\u7B49\u6388\u6743\u7684\u65F6\u95F4\u4E0D\u8BA1\u5165\u9884\u7B97\uFF09\uFF1B\u8BBE\u7F6E\u9875\u4E09\u9009\u4E00\u662F\u786C\u6027\u8981\u6C42\uFF0C\u6CA1\u58F0\u660E\u5C31\u4E0D\u80FD\u8FD0\u884C\u3002\u6BCF\u9879\u80FD\u529B\u90FD\u8981\u4E24\u8FB9\u70B9\u5934\u624D\u751F\u6548\uFF1A\u63D2\u4EF6\u5728 plugin.json \u91CC\u58F0\u660E\u8FC7\uFF0C\u4F60\u5728\u300C\u6743\u9650\u300D\u91CC\u653E\u884C\u2014\u2014\u6CA1\u58F0\u660E\u7684\u5141\u8BB8\u4E86\u4E5F\u4E0D\u7ED9\uFF0C\u58F0\u660E\u4E86\u4F46\u88AB\u7981\u6B62\u7684\u4E00\u5F8B\u62E6\u4E0B\u3002" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime50.jsx)(Card, { title: "\u5B89\u88C5\u63D2\u4EF6", children: /* @__PURE__ */ (0, import_jsx_runtime50.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-3)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime50.jsxs)("div", { style: { display: "flex", flexWrap: "wrap", alignItems: "center", gap: "var(--w-space-3)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime50.jsx)(SignalButton, { onClick: hostOnly, children: "\u4ECE zip \u5B89\u88C5" }),
          /* @__PURE__ */ (0, import_jsx_runtime50.jsx)(GlassButton, { size: "md", onClick: hostOnly, children: "\u4ECE\u6587\u4EF6\u5939\u5B89\u88C5" }),
          /* @__PURE__ */ (0, import_jsx_runtime50.jsx)(GlassButton, { size: "md", onClick: hostOnly, children: "\u6253\u5F00\u63D2\u4EF6\u76EE\u5F55" })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime50.jsx)("span", { style: { ...CAPTION3, lineHeight: 1.6 }, children: "zip \u7684\u6839\u76EE\u5F55\u3001\u6216\u6839\u76EE\u5F55\u91CC\u90A3\u5C42\u540C\u540D\u6587\u4EF6\u5939\u91CC\uFF0C\u90FD\u8981\u6709\u4E00\u4E2A plugin.json\uFF08\u5199 name\u3001settings \u4E09\u9009\u4E00\u3001hooks \u6307\u5411\u5305\u5185\u7684 JS\uFF09\u3002\u540C\u540D\u63D2\u4EF6\u4F1A\u88AB\u76F4\u63A5\u8986\u76D6\uFF0C\u4E0D\u4F1A\u53E6\u5F00\u4E00\u4EFD\u3002" }),
        /* @__PURE__ */ (0, import_jsx_runtime50.jsxs)("span", { style: { ...CAPTION3, lineHeight: 1.6 }, children: [
          "\u63D2\u4EF6\u88C5\u5728 ",
          PLUGINS_DIR_LABEL,
          " \u2014\u2014 \u76F4\u63A5\u62F7\u8FDB\u8FD9\u4E2A\u76EE\u5F55\u518D\u70B9\u53F3\u4E0A\u89D2\u300C\u5237\u65B0\u300D\u4E5F\u4E00\u6837\u88C5\u5F97\u4E0A\u3002"
        ] })
      ] }) }),
      /* @__PURE__ */ (0, import_jsx_runtime50.jsx)("span", { style: { ...CAPTION3, lineHeight: 1.6 }, children: "\u8FD8\u6CA1\u6709\u63D2\u4EF6\u3002\u53EF\u4EE5\u7528\u4E0B\u9762\u7684\u5DE5\u574A\u751F\u6210\u4E00\u4E2A\uFF0C\u6216\u7528\u4E0A\u9762\u90A3\u4E24\u9897\u6309\u94AE\u88C5\u73B0\u6210\u7684\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime50.jsx)(Card, { title: "\u63D2\u4EF6\u5DE5\u574A", children: /* @__PURE__ */ (0, import_jsx_runtime50.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-3)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime50.jsx)("span", { style: { ...CAPTION3, lineHeight: 1.6 }, children: "\u7528\u4E00\u53E5\u8BDD\u8BF4\u4F60\u8981\u4EC0\u4E48\uFF0CAI \u4F1A\u5199\u51FA\u6E05\u5355\u3001\u8BBE\u7F6E\u9879\u548C JS \u94A9\u5B50\uFF0C\u5E76\u76F4\u63A5\u5B89\u88C5\u3002\u88C5\u5B8C\u53EF\u4EE5\u70B9\u300C\u8BD5\u8DD1\u300D\uFF1B\u574F\u4E86\u5C31\u5230\u300C\u65E5\u5FD7\u300D\u770B\u539F\u56E0\uFF0C\u4E0D\u5F71\u54CD\u4E3B\u7A0B\u5E8F\u3002" }),
        /* @__PURE__ */ (0, import_jsx_runtime50.jsx)(
          Field,
          {
            value: workshopPrompt,
            onChange: setWorkshopPrompt,
            multiline: true,
            label: "\u63D2\u4EF6\u5DE5\u574A",
            placeholder: "\u4F8B\uFF1A\u8BA9 TA \u5728\u56DE\u590D\u672B\u5C3E\u5076\u5C14\u52A0\u4E00\u53E5\u6781\u7B80\u7684\u73AF\u5883\u63CF\u5199\uFF1B\u7ED9\u4E00\u4E2A\u300C\u514B\u5236\u7A0B\u5EA6\u300D\u6ED1\u6746"
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime50.jsx)("div", { children: /* @__PURE__ */ (0, import_jsx_runtime50.jsx)(SignalButton, { onClick: hostOnly, children: "\u751F\u6210\u5E76\u5B89\u88C5" }) }),
        /* @__PURE__ */ (0, import_jsx_runtime50.jsx)("span", { style: { ...CAPTION3, lineHeight: 1.6, opacity: 0.8 }, children: "\u63D2\u4EF6\u80FD\u505A\u4EC0\u4E48\uFF1AREADME \u91CC\u7684 plugin.json \u6E05\u5355\uFF08hooks \u6307\u5411 JS \u6587\u4EF6\uFF09+ \u6C99\u7BB1\u8FD0\u884C\u65F6\u3002\u5B83\u4EEC\u53EF\u4EE5\u4E92\u76F8\u4F9D\u8D56\uFF1B\u7F3A\u4F9D\u8D56\u4E14\u7ED9\u4E86\u5B89\u88C5\u5730\u5740\u65F6\uFF0C\u4E0A\u9762\u7684\u300C\u5B89\u88C5\u5730\u5740\u300D\u4F1A\u76F4\u63A5\u6253\u5F00\u3002\u6DF1\u5EA6\u6539\u5199\u4ECD\u53D7\u6743\u9650\u4E0E\u6C99\u7BB1\u7EA6\u675F\u2014\u2014\u8FD9\u662F\u6709\u610F\u4E3A\u4E4B\uFF1A\u63D2\u4EF6\u4E0D\u8BE5\u80FD\u6084\u6084\u66FF\u6362\u4E3B\u7A0B\u5E8F\u3002" })
      ] }) }),
      /* @__PURE__ */ (0, import_jsx_runtime50.jsx)(Card, { title: "MCP \u5DE5\u574A", children: /* @__PURE__ */ (0, import_jsx_runtime50.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-3)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime50.jsx)("span", { style: { ...CAPTION3, lineHeight: 1.6 }, children: "\u7528\u4E00\u53E5\u8BDD\u63CF\u8FF0\u4F60\u8981\u7684\u5916\u90E8\u80FD\u529B\uFF0CAI \u4F1A\u751F\u6210\u4E00\u4E2A MCP \u6570\u636E\u5305\uFF08\u670D\u52A1\u5668 + \u82E5\u5E72\u5DE5\u5177\uFF09\uFF0C\u76F4\u63A5\u6CE8\u518C\u5E76\u8BD5\u8DD1\u3002\u672C\u5730\u5DE5\u5177\u8DD1\u5728\u53D7\u9650 JS \u6C99\u7BB1\u91CC\uFF1B\u53EA\u6709\u7ED9\u4E86\u8FDC\u7A0B\u5730\u5740\u7684\u5DE5\u5177\u624D\u4F1A\u8D70 HTTP\u3002\u88C5\u597D\u540E AI \u7528 mcp_list / mcp_call \u8C03\u7528\u3002" }),
        /* @__PURE__ */ (0, import_jsx_runtime50.jsx)(
          Field,
          {
            value: mcpPrompt,
            onChange: setMcpPrompt,
            multiline: true,
            label: "MCP \u5DE5\u574A",
            placeholder: "\u4F8B\uFF1A\u505A\u4E00\u4E2A\u300C\u60C5\u7EEA\u8272\u5F69\u300D\u5DE5\u5177\uFF0C\u628A\u4E00\u53E5\u8BDD\u6620\u5C04\u6210\u4E00\u79CD\u989C\u8272\u548C\u4E00\u53E5\u73AF\u5883\u63CF\u5199\uFF1B\u518D\u505A\u300C\u968F\u673A\u5C0F\u5267\u573A\u300D\u7ED9\u4E09\u4E2A\u5F00\u573A"
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime50.jsx)("div", { children: /* @__PURE__ */ (0, import_jsx_runtime50.jsx)(SignalButton, { onClick: hostOnly, children: "\u751F\u6210\u5E76\u6CE8\u518C" }) }),
        /* @__PURE__ */ (0, import_jsx_runtime50.jsx)(Sep, { bleed: true }),
        /* @__PURE__ */ (0, import_jsx_runtime50.jsx)("span", { style: { fontSize: "var(--w-size-subhead)", color: "var(--w-ink-display)" }, children: "\u5DF2\u88C5 MCP \u6570\u636E\u5305" }),
        /* @__PURE__ */ (0, import_jsx_runtime50.jsx)("span", { style: { ...CAPTION3, lineHeight: 1.6 }, children: "\u6570\u636E\u5305\uFF1D\u81EA\u5DF1\u5199\u7684 tools.json\uFF08\u672C\u5730 JS \u5DE5\u5177\u6216\u8FDC\u7A0B HTTP \u7AEF\u70B9\uFF09\uFF0C\u662F\u5F80\u673A\u5668\u91CC\u88C5\u4E1C\u897F\uFF0C\u6240\u4EE5\u548C\u63D2\u4EF6\u653E\u5728\u4E00\u8D77\u3002\u8981\u63A5\u7684\u662F\u5916\u90E8 MCP \u670D\u52A1\uFF08\u672C\u673A\u542F\u52A8\u8FDB\u7A0B\u6216\u8FDE\u4E00\u4E2A\u5730\u5740\uFF09\uFF0C\u53BB\u8BBE\u7F6E \u2192 \u5916\u90E8\u4E0E\u63A5\u5165 \u2192 MCP\u3002" }),
        /* @__PURE__ */ (0, import_jsx_runtime50.jsxs)("div", { style: { display: "flex", flexWrap: "wrap", alignItems: "center", gap: "var(--w-space-3)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime50.jsx)(GlassButton, { size: "sm", onClick: hostOnly, children: "\u5BFC\u5165\u6570\u636E\u5305 zip" }),
          /* @__PURE__ */ (0, import_jsx_runtime50.jsx)(GlassButton, { size: "sm", onClick: hostOnly, children: "\u4ECE\u6587\u4EF6\u5939\u5BFC\u5165" }),
          /* @__PURE__ */ (0, import_jsx_runtime50.jsx)(GlassButton, { size: "sm", onClick: hostOnly, children: "\u4ECE GitHub \u5BFC\u5165" })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime50.jsx)("span", { style: { ...CAPTION3, lineHeight: 1.6 }, children: "\u8FD8\u6CA1\u6709 MCP \u6570\u636E\u5305\u3002\u53EF\u4EE5\u5728\u4E0A\u9762\u7528\u4E00\u53E5\u8BDD\u751F\u6210\uFF0C\u6216\u7528\u8FD9\u4E09\u9897\u6309\u94AE\u5BFC\u5165\u73B0\u6210\u7684\u3002" })
      ] }) })
    ] })
  ] });
}

// src/screens/settings-ai/pluginPerms.tsx
var import_react39 = __toESM(require_react(), 1);
var import_jsx_runtime51 = __toESM(require_jsx_runtime(), 1);
var GRANT_LABEL = { allow: "\u5141\u8BB8", ask: "\u95EE\u4E00\u6B21", deny: "\u7981\u6B62" };
var CAPABILITIES = [
  {
    name: "settings",
    title: "\u81EA\u5DF1\u7684\u8BBE\u7F6E",
    detail: "\u8BFB\u4E0E\u5199 plugins/<\u63D2\u4EF6>/settings.json \u91CC\u5C5E\u4E8E\u5B83\u81EA\u5DF1\u7684\u90A3\u51E0\u9879\u3002\u770B\u4E0D\u5230\u6E38\u620F\u8BBE\u7F6E\uFF0C\u4E5F\u770B\u4E0D\u5230\u522B\u7684\u63D2\u4EF6\u7684\u8BBE\u7F6E\u3002",
    tier: "own",
    defaultGrant: "allow",
    argHint: ""
  },
  {
    name: "storage",
    title: "\u81EA\u5DF1\u7684\u6587\u4EF6\u533A",
    detail: "\u5728 plugins/<\u63D2\u4EF6>/files/ \u4E0B\u9762\u5199\u5C0F\u6587\u4EF6\uFF08\u5355\u4E2A 1MB\u3001\u5408\u8BA1 8MB\uFF09\u3002\u51FA\u4E86\u8FD9\u4E2A\u76EE\u5F55\u4E00\u5F8B\u62D2\u7EDD\uFF0C\u8BFB\u5199\u4E0D\u5230\u5B58\u6863\u3001\u56FE\u7247\u4E0E\u8BBE\u7F6E\u3002",
    tier: "own",
    defaultGrant: "allow",
    argHint: ""
  },
  {
    name: "log",
    title: "\u5199\u5E94\u7528\u65E5\u5FD7",
    detail: "\u5F80\u5E94\u7528\u65E5\u5FD7\u8FFD\u52A0\u4E00\u884C\uFF08\u81EA\u52A8\u5E26\u4E0A\u63D2\u4EF6\u540D\u524D\u7F00\uFF09\u3002\u65E5\u5FD7\u4F1A\u968F\u6570\u636E\u5305\u5BFC\u51FA\uFF0C\u522B\u628A\u7528\u6237\u7684\u79C1\u5BC6\u5185\u5BB9\u5199\u8FDB\u53BB\u3002",
    tier: "own",
    defaultGrant: "allow",
    argHint: ""
  },
  {
    name: "prompt.write",
    title: "\u6CE8\u5165\u4EBA\u8BBE\u4E0E\u4E0A\u4E0B\u6587",
    detail: "persona / context \u94A9\u5B50\u5F80\u63D0\u793A\u8BCD\u91CC\u52A0\u6587\u5B57\u3002\u8BBE\u4E3A\u7981\u6B62\u540E\u8FD9\u4E24\u4E2A\u94A9\u5B50\u76F4\u63A5\u4E0D\u6267\u884C\u2014\u2014\u5B83\u552F\u4E00\u7684\u4F5C\u7528\u5C31\u662F\u52A0\u5B57\u3002",
    tier: "pipeline",
    defaultGrant: "allow",
    argHint: ""
  },
  {
    name: "pipeline.transform",
    title: "\u6539\u5199\u5BF9\u8BDD\u6587\u672C",
    detail: "beforeSend / afterReply \u7684\u8FD4\u56DE\u503C\u66FF\u6362\u539F\u6587\u3002\u8FD9\u662F\u300C\u6539\u5199\u8FD9\u4E00\u53E5\u300D\uFF0C\u4E0D\u662F\u300C\u63A5\u7BA1\u7BA1\u7EBF\u300D\u3002\u8BBE\u4E3A\u7981\u6B62\u540E\u94A9\u5B50\u4E0D\u6267\u884C\uFF0C\u6587\u672C\u539F\u6837\u6D41\u8FC7\u3002",
    tier: "pipeline",
    defaultGrant: "allow",
    argHint: ""
  },
  {
    name: "pipeline.override",
    title: "\u6574\u6BB5\u63A5\u7BA1\u7BA1\u7EBF",
    detail: "\u58F0\u660E\u4E86 overridePersona \u7684\u63D2\u4EF6\u4F1A\u6E05\u7A7A\u522B\u7684\u4EBA\u8BBE\u6BB5\uFF0C\u53EA\u7559\u81EA\u5DF1\u90A3\u4EFD\u3002\u7B49\u4E8E\u66FF\u4E3B\u7A0B\u5E8F\u51B3\u5B9A\u300CTA \u662F\u8C01\u300D\u3002",
    tier: "pipeline",
    defaultGrant: "ask",
    argHint: ""
  },
  {
    name: "tools",
    title: "\u8C03\u7528\u5DE5\u5177",
    detail: "callTool \u8BA9\u5BBF\u4E3B\u6267\u884C\u4E00\u4E2A\u5DE5\u5177\uFF08\u5185\u7F6E\u3001\u5916\u90E8\u3001MCP \u90FD\u7B97\uFF09\u3002\u5DE5\u5177\u80FD\u505A\u7684\u4E8B\u6BD4 JS \u591A\u5F97\u591A\uFF0C\u6240\u4EE5\u8981\u9010\u4E2A\u5DE5\u5177\u540D\u653E\u884C\u3002",
    tier: "interact",
    defaultGrant: "ask",
    argHint: "\u5DE5\u5177\u540D\uFF0C\u5982 tools:send_to_user"
  },
  {
    name: "plugins",
    title: "\u8C03\u7528\u5176\u5B83\u63D2\u4EF6",
    detail: "callPlugin \u8FDB\u5165\u53E6\u4E00\u4E2A\u63D2\u4EF6\u7684\u94A9\u5B50\u3002\u88AB\u8C03\u7528\u65B9\u4ECD\u7136\u53D7\u81EA\u5DF1\u90A3\u4EFD\u6388\u6743\u7EA6\u675F\uFF0C\u4E0D\u4F1A\u501F\u7740\u522B\u4EBA\u7684\u624B\u8D8A\u6743\u3002",
    tier: "interact",
    defaultGrant: "ask",
    argHint: "\u63D2\u4EF6 id\uFF0C\u5982 plugins:mood_echo"
  },
  {
    name: "broadcast",
    title: "\u5E7F\u64AD\u4E0E\u6536\u542C",
    detail: "emit(topic, payload) \u53D1\u7ED9\u5176\u5B83\u63D2\u4EF6\u7684 on:<topic> \u94A9\u5B50\uFF0C\u8BDD\u9898\u540D\u8981\u9010\u6761\u58F0\u660E\u3002\u7981\u6B62\u540E emit \u76F4\u63A5\u88AB\u62E6\u3002",
    tier: "interact",
    defaultGrant: "ask",
    argHint: "\u8BDD\u9898\u540D\uFF0C\u5982 broadcast:turn.done"
  },
  {
    name: "notify",
    title: "\u53D1\u7528\u6237\u901A\u77E5",
    detail: "\u5728\u5E94\u7528\u91CC\u5F39\u4E00\u6761\u7528\u6237\u53EF\u89C1\u7684\u63D0\u793A\u3002\u6253\u6270\u5F52\u6253\u6270\uFF0C\u4F46\u5B83\u4E0D\u6539\u6570\u636E\uFF0C\u6240\u4EE5\u9ED8\u8BA4\u95EE\u4E00\u6B21\u3002",
    tier: "interact",
    defaultGrant: "ask",
    argHint: ""
  },
  {
    name: "stats",
    title: "\u8C03\u6574\u89D2\u8272\u6570\u503C",
    detail: "adjustStat \u76F4\u63A5\u6539\u52A8\u67D0\u4E2A\u89D2\u8272\u7684\u597D\u611F / \u4FE1\u4EFB / \u7CBE\u529B\uFF08\u8D70\u4E0E\u4E3B\u6D41\u7A0B\u540C\u4E00\u5957\u94B3\u5236\u4E0E\u843D\u76D8\uFF09\u3002\u8FD9\u662F\u6539\u6570\u636E\uFF0C\u4E0D\u662F\u5F39\u63D0\u793A\uFF0C\u6240\u4EE5\u9010\u4E2A\u89D2\u8272\u653E\u884C\u3002",
    tier: "interact",
    defaultGrant: "ask",
    argHint: "\u89D2\u8272 id\uFF0C\u5982 stats:mom"
  },
  {
    name: "page",
    title: "\u6253\u5F00\u81EA\u5E26\u9875\u9762",
    detail: "manifest.pages \u58F0\u660E\u7684\u9875\u9762\uFF0C\u7528\u7CFB\u7EDF\u6D4F\u89C8\u5668\u6253\u5F00\u3002\u70B9\u4E86\u624D\u5F00\uFF0C\u63D2\u4EF6\u81EA\u5DF1\u5F39\u4E0D\u51FA\u6765\u3002",
    tier: "interact",
    defaultGrant: "allow",
    argHint: ""
  },
  {
    name: "network",
    title: "\u8BBF\u95EE\u5916\u90E8\u7F51\u7EDC",
    detail: "fetch \u51FA\u7F51\u3002\u5FC5\u987B\u9010\u4E3B\u673A\u5728 plugin.json \u91CC\u58F0\u660E\uFF08network:api.example.com\uFF09\uFF0C\u4E0D\u8DDF\u8DF3\u8F6C\uFF0C\u53EA\u7ED9 http/https\u3002",
    tier: "external",
    defaultGrant: "deny",
    argHint: "\u4E3B\u673A\u540D\uFF0C\u5982 network:api.example.com"
  },
  {
    name: "localnet",
    title: "\u8BBF\u95EE\u672C\u673A\u4E0E\u5185\u7F51",
    detail: "\u5141\u8BB8 fetch \u6253\u5230 127.0.0.1\u3001\u5C40\u57DF\u7F51\u4E0E 169.254 \u8FD9\u7C7B\u5730\u5740\u2014\u2014\u672C\u673A\u670D\u52A1\u5F80\u5F80\u6CA1\u9274\u6743\uFF0C\u8FD9\u4E00\u6761\u6BD4\u5916\u7F51\u66F4\u5371\u9669\u3002",
    tier: "external",
    defaultGrant: "deny",
    argHint: ""
  }
];
var UNO = CAPABILITIES.filter((capability) => capability.tier === "own");
var PIPELINE = CAPABILITIES.filter((capability) => capability.tier === "pipeline");
var INTERACT = CAPABILITIES.filter((capability) => capability.tier === "interact");
var EXTERNAL = CAPABILITIES.filter((capability) => capability.tier === "external");
var CAPTION4 = { fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)" };
function TierHead({ bar, title: title2, note }) {
  return /* @__PURE__ */ (0, import_jsx_runtime51.jsxs)(import_jsx_runtime51.Fragment, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime51.jsxs)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-3)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime51.jsx)("span", { style: { width: "4px", height: "18px", borderRadius: "1px", background: bar }, "aria-hidden": "true" }),
      /* @__PURE__ */ (0, import_jsx_runtime51.jsx)("span", { style: { flex: 1, fontSize: "var(--w-size-subhead)", fontWeight: 700, color: "var(--w-ink-display)" }, children: title2 }),
      note ? /* @__PURE__ */ (0, import_jsx_runtime51.jsx)("span", { style: CAPTION4, children: note }) : null
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime51.jsx)(Sep, {})
  ] });
}
function PluginPerms({ query, back }) {
  const pluginName = query.get("name") ?? "\u793A\u4F8B\u63D2\u4EF6";
  const pluginVersion = query.get("version") ?? "1.0";
  const [policies, setPolicies] = (0, import_react39.useState)(
    () => Object.fromEntries(CAPABILITIES.map((capability) => [capability.name, capability.defaultGrant]))
  );
  const [status, setStatus] = (0, import_react39.useState)("");
  const [auditVisible, setAuditVisible] = (0, import_react39.useState)(false);
  const custom = CAPABILITIES.filter((capability) => policies[capability.name] !== capability.defaultGrant).length;
  const summary = `${CAPABILITIES.length} \u9879\u80FD\u529B\u91CC\uFF0C\u8FD9\u4E2A\u63D2\u4EF6\u58F0\u660E\u4E86 0 \u9879\uFF0C\u73B0\u5728\u80FD\u7528 0 \u9879` + (custom === 0 ? "\uFF0C\u4F60\u8FD8\u6CA1\u6539\u8FC7\u4EFB\u4F55\u4E00\u6761\uFF08\u5168\u6309\u9ED8\u8BA4\uFF09\u3002" : `\uFF0C\u4F60\u6539\u8FC7 ${custom} \u6761\u3002`);
  const setGrant = (capability, grant) => {
    setPolicies((prev) => ({ ...prev, [capability.name]: grant }));
    setStatus(`\u300C${capability.title}\u300D\u5DF2\u8BBE\u4E3A${GRANT_LABEL[grant]}\u3002\uFF08\u8FD9\u4E2A\u63D2\u4EF6\u6CA1\u58F0\u660E\u5B83\uFF0C\u58F0\u660E\u4E86\u624D\u7528\u5F97\u4E0A\u3002\uFF09`);
  };
  const rowsOf = (list) => list.map((capability) => {
    const policy = policies[capability.name];
    return /* @__PURE__ */ (0, import_jsx_runtime51.jsxs)(Card, { children: [
      /* @__PURE__ */ (0, import_jsx_runtime51.jsxs)("div", { style: { display: "flex", alignItems: "flex-start", gap: "var(--w-space-3)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime51.jsxs)("div", { style: { flex: 1, minWidth: 0, display: "flex", flexDirection: "column", gap: "2px" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime51.jsx)("span", { style: { fontSize: "var(--w-size-body)", color: "var(--w-ink-display)" }, children: capability.title }),
          /* @__PURE__ */ (0, import_jsx_runtime51.jsx)("span", { style: { ...CAPTION4, lineHeight: 1.6 }, children: capability.detail })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime51.jsx)(Chip, { children: "\u7528\u4E0D\u4E0A" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime51.jsx)("p", { style: { margin: "var(--w-space-2) 0 0", ...CAPTION4, lineHeight: 1.6 }, children: "\u63D2\u4EF6\u6CA1\u58F0\u660E\u8FD9\u4E00\u9879\uFF1A\u5141\u8BB8\u4E86\u4E5F\u4E0D\u4F1A\u7ED9\u5B83\u2014\u2014\u58F0\u660E\u624D\u662F\u53EF\u5BA1\u8BA1\u7684\u3002" }),
      /* @__PURE__ */ (0, import_jsx_runtime51.jsxs)("div", { style: { display: "flex", flexWrap: "wrap", alignItems: "center", gap: "var(--w-space-2)", marginTop: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime51.jsx)(TextBtn, { tone: policy === "deny" ? "signal" : "plain", onClick: () => setGrant(capability, "deny"), children: "\u7981\u6B62" }),
        /* @__PURE__ */ (0, import_jsx_runtime51.jsx)(TextBtn, { tone: policy === "ask" ? "signal" : "plain", onClick: () => setGrant(capability, "ask"), children: "\u95EE\u4E00\u6B21" }),
        /* @__PURE__ */ (0, import_jsx_runtime51.jsx)(TextBtn, { tone: policy === "allow" ? "signal" : "plain", onClick: () => setGrant(capability, "allow"), children: "\u5141\u8BB8" }),
        /* @__PURE__ */ (0, import_jsx_runtime51.jsx)(TextBtn, { onClick: () => setStatus(`\u300C${capability.title}\u300D\u4E0B\u6B21\u7528\u5230\u65F6\u4F1A\u91CD\u65B0\u95EE\u3002`), children: "\u91CD\u65B0\u95EE" })
      ] })
    ] }, capability.name);
  });
  return /* @__PURE__ */ (0, import_jsx_runtime51.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime51.jsx)(
      PageHeader,
      {
        title: "\u63D2\u4EF6\u6743\u9650",
        subtitle: `${pluginName} v${pluginVersion}`,
        onBack: back,
        backText: "< \u8FD4\u56DE",
        right: /* @__PURE__ */ (0, import_jsx_runtime51.jsx)(
          GlassButton,
          {
            size: "sm",
            onClick: () => {
              if (!window.confirm(`\u6062\u590D\u9ED8\u8BA4
\u64A4\u9500\u4F60\u5BF9\u300C${pluginName}\u300D\u505A\u8FC7\u7684\u5168\u90E8\u6388\u6743\u6539\u52A8\uFF1F\u95EE\u8FC7\u4E4B\u540E\u7684\u7B54\u6848\u4E5F\u4E00\u8D77\u6E05\u6389\u3002`)) return;
              setPolicies(
                Object.fromEntries(CAPABILITIES.map((capability) => [capability.name, capability.defaultGrant]))
              );
              setStatus("\u5DF2\u6062\u590D\u9ED8\u8BA4\u7B56\u7565\u3002");
            },
            children: "\u6062\u590D\u9ED8\u8BA4"
          }
        )
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime51.jsxs)("div", { className: "screen", style: { maxWidth: "920px" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime51.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime51.jsx)("span", { style: { fontSize: "var(--w-size-body)", color: "var(--w-ink-body)" }, children: summary }),
        /* @__PURE__ */ (0, import_jsx_runtime51.jsx)("span", { style: { ...CAPTION4, lineHeight: 1.6 }, children: "\u4E00\u6761\u80FD\u529B\u8981\u7528\uFF0C\u5F97\u540C\u65F6\u6EE1\u8DB3\u4E24\u4EF6\u4E8B\uFF1A\u8FD9\u4E2A\u63D2\u4EF6\u88AB\u5141\u8BB8\uFF0C\u800C\u4E14\u5B83\u786E\u5B9E\u8981\u8FC7\u2014\u2014\u8D8A\u51FA\u81EA\u5DF1\u5BB6\u95E8\u7684\u7528\u6CD5\uFF08\u5DE5\u5177\u3001\u522B\u7684\u63D2\u4EF6\u3001\u5E7F\u64AD\u3001\u8054\u7F51\uFF09\u5FC5\u987B\u5728 plugin.json \u91CC\u5199\u660E\uFF0C\u6CA1\u5199\u660E\u7684\u5373\u4F7F\u4F60\u70B9\u300C\u5141\u8BB8\u300D\u4E5F\u4E0D\u4F1A\u7ED9\u5B83\u3002\u300C\u53EA\u5728\u81EA\u5DF1\u5BB6\u91CC\u300D\u90A3\u4E00\u7EC4\u4E0D\u8981\u6C42\u58F0\u660E\uFF1A\u5B83\u78B0\u7684\u53EA\u6709\u8FD9\u4E2A\u63D2\u4EF6\u81EA\u5DF1\u7684\u8BBE\u7F6E\u9879\u548C\u6587\u4EF6\u533A\uFF0C\u5708\u662F\u7A0B\u5E8F\u5212\u7684\uFF0C\u4F5C\u8005\u91CD\u590D\u58F0\u660E\u4E00\u904D\u4E70\u4E0D\u5230\u4EFB\u4F55\u4FDD\u8BC1\u3002" }),
        /* @__PURE__ */ (0, import_jsx_runtime51.jsx)("span", { style: { ...CAPTION4, lineHeight: 1.6 }, children: "\u8FD9\u4EFD\u6E05\u5355\u91CC\u6CA1\u6709 permissions \u6570\u7EC4\uFF1A\u5B83\u5199\u4E8E\u6743\u9650\u76EE\u5F55\u4E4B\u524D\uFF0C\u6240\u4EE5\u9664\u300C\u51FA\u672C\u673A\u300D\u5916\u7684\u80FD\u529B\u6309\u9ED8\u8BA4\u7B56\u7565\u5BF9\u5F85\uFF08\u8BE5\u95EE\u7684\u7167\u95EE\u3001\u8BE5\u7981\u7684\u7167\u7981\uFF09\u3002\u53EA\u8981\u5B83\u8865\u4E0A\u4EFB\u4F55\u4E00\u6761\u58F0\u660E\uFF0C\u5C31\u53EA\u8BA4\u58F0\u660E\u8FC7\u7684\u90A3\u51E0\u6761\u3002" }),
        status ? /* @__PURE__ */ (0, import_jsx_runtime51.jsx)("span", { style: { fontSize: "var(--w-size-caption)", color: "var(--w-signal)" }, children: status }) : null
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime51.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-3)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime51.jsx)(TierHead, { bar: "var(--w-ink-faint)", title: "\u53EA\u5728\u81EA\u5DF1\u5BB6\u91CC \xB7 \u9ED8\u8BA4\u5141\u8BB8", note: "\u8FD9\u4E00\u7EC4\u4E0D\u8981\u6C42\u58F0\u660E\uFF0C\u7981\u6B62\u4ECD\u7136\u751F\u6548" }),
        rowsOf(UNO)
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime51.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-3)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime51.jsx)(TierHead, { bar: "var(--w-signal)", title: "\u52A8\u4E3B\u7A0B\u5E8F\u7684\u7BA1\u7EBF \xB7 \u9ED8\u8BA4\u5141\u8BB8\uFF0C\u63A5\u7BA1\u8981\u95EE" }),
        rowsOf(PIPELINE)
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime51.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-3)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime51.jsx)(TierHead, { bar: "var(--w-signal)", title: "\u52A8\u7528\u522B\u7684\u4E1C\u897F \xB7 \u9ED8\u8BA4\u8981\u95EE", note: "\u5DE5\u5177\u3001\u522B\u7684\u63D2\u4EF6\u3001\u5E7F\u64AD\u3001\u901A\u77E5" }),
        rowsOf(INTERACT)
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime51.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-3)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime51.jsx)(TierHead, { bar: "var(--w-ink-display)", title: "\u51FA\u672C\u673A \xB7 \u9ED8\u8BA4\u7981\u6B62", note: "\u9010\u4E3B\u673A\u58F0\u660E\uFF0C\u9010\u4E3B\u673A\u653E\u884C" }),
        rowsOf(EXTERNAL)
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime51.jsxs)(Card, { title: "\u5224\u5B9A\u7559\u75D5", right: /* @__PURE__ */ (0, import_jsx_runtime51.jsx)(TextBtn, { onClick: () => setAuditVisible((prev) => !prev), children: auditVisible ? "\u6536\u8D77" : "\u5C55\u5F00" }), children: [
        /* @__PURE__ */ (0, import_jsx_runtime51.jsx)("span", { style: { ...CAPTION4, lineHeight: 1.6 }, children: "\u6BCF\u4E00\u6B21\u62E6\u622A\u3001\u6BCF\u4E00\u6B21\u95EE\u8FC7\u4E4B\u540E\u653E\u884C\u90FD\u8BB0\u5728\u8FD9\u91CC\uFF08\u540C\u4E00\u6761\u53CD\u590D\u649E\u4E0A\u53EA\u8BA1\u6B21\uFF09\u3002" }),
        /* @__PURE__ */ (0, import_jsx_runtime51.jsx)("p", { style: { margin: "var(--w-space-2) 0 0", ...CAPTION4, lineHeight: 1.6 }, children: "\u8FD8\u6CA1\u6709\u5224\u5B9A\u8BB0\u5F55\uFF1A\u8FD9\u4E2A\u63D2\u4EF6\u81F3\u4ECA\u6CA1\u649E\u4E0A\u4EFB\u4F55\u4E00\u9053\u95F8\u3002" })
      ] })
    ] })
  ] });
}

// src/screens/settings-ai/pluginSettings.tsx
var import_react40 = __toESM(require_react(), 1);
var import_jsx_runtime52 = __toESM(require_jsx_runtime(), 1);
var EXAMPLE_FIELDS = [
  {
    key: "greet",
    label: "\u95EE\u5019\u8BED",
    type: "text",
    default: "\u4ECA\u5929\u4E5F\u8981\u597D\u597D\u4F11\u606F\u3002",
    help: "\u56DE\u590D\u5F00\u5934\u52A0\u7684\u90A3\u4E00\u53E5\uFF0C\u7559\u7A7A\u5C31\u4E0D\u52A0\u3002",
    options: []
  },
  {
    key: "enabled",
    label: "\u542F\u7528\u94A9\u5B50",
    type: "switch",
    default: "true",
    help: "\u5173\u6389\u540E\u94A9\u5B50\u4E0D\u6267\u884C\uFF0C\u63D2\u4EF6\u4ECD\u7136\u7559\u5728\u5217\u8868\u91CC\u3002",
    options: []
  },
  {
    key: "everyTurns",
    label: "\u8F6E\u6B21\u95F4\u9694",
    type: "number",
    default: "8",
    help: "\u6BCF\u591A\u5C11\u8F6E\u505A\u4E00\u6B21\uFF0C\u592A\u5C0F\u4F1A\u663E\u5F97\u5570\u55E6\u3002",
    options: []
  },
  { key: "tone", label: "\u8BED\u6C14", type: "select", default: "\u6E29\u67D4", help: "\u6539\u5199\u6587\u672C\u65F6\u7684\u53E3\u543B\u3002", options: ["\u6E29\u67D4", "\u514B\u5236", "\u6D3B\u6CFC"] }
];
var CAPTION5 = { fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)" };
var FIELD_LABEL2 = { fontSize: "var(--w-size-small)", color: "var(--w-ink-body)" };
function PluginSettings({ query, back }) {
  const pluginId = query.get("id") ?? "";
  const title2 = `${pluginId || "\u793A\u4F8B\u63D2\u4EF6"} \xB7 \u8BBE\u7F6E`;
  const description = query.get("desc") ?? "\u4E00\u4EFD\u793A\u4F8B\u6E05\u5355\uFF1A\u6587\u672C\u3001\u5F00\u5173\u3001\u6570\u5B57\u3001\u4E0B\u62C9\u5404\u4E00\uFF0C\u5B9E\u9645\u8868\u5355\u7531 plugin.json \u7684 fields \u73B0\u573A\u751F\u6210\u3002";
  const [values, setValues] = (0, import_react40.useState)(
    () => Object.fromEntries(EXAMPLE_FIELDS.map((field) => [field.key, field.default]))
  );
  const [initial, setInitial] = (0, import_react40.useState)(values);
  const [status, setStatus] = (0, import_react40.useState)("");
  const setValue = (key, next) => setValues((prev) => ({ ...prev, [key]: next }));
  const save = () => {
    const dirty = EXAMPLE_FIELDS.filter((field) => (values[field.key] ?? "") !== (initial[field.key] ?? ""));
    if (dirty.length === 0) {
      setStatus("\u6CA1\u6709\u6539\u52A8\uFF0C\u4EC0\u4E48\u90FD\u4E0D\u7528\u5199\u3002");
      return;
    }
    setInitial({ ...values });
    setStatus("\u5DF2\u4FDD\u5B58\uFF0C\u4E0B\u4E00\u6B21\u7528\u5230\u94A9\u5B50\u65F6\u751F\u6548\u3002");
  };
  return /* @__PURE__ */ (0, import_jsx_runtime52.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime52.jsx)(PageHeader, { title: title2, onBack: back, backText: "< \u8FD4\u56DE" }),
    /* @__PURE__ */ (0, import_jsx_runtime52.jsxs)("div", { className: "screen screen--narrow", children: [
      /* @__PURE__ */ (0, import_jsx_runtime52.jsx)("span", { style: { ...CAPTION5, lineHeight: 1.6 }, children: description }),
      /* @__PURE__ */ (0, import_jsx_runtime52.jsx)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-3)" }, children: EXAMPLE_FIELDS.map((field) => /* @__PURE__ */ (0, import_jsx_runtime52.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-1)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime52.jsx)("span", { style: FIELD_LABEL2, children: field.label || field.key }),
        field.help ? /* @__PURE__ */ (0, import_jsx_runtime52.jsx)("span", { style: CAPTION5, children: field.help }) : null,
        field.type === "switch" ? /* @__PURE__ */ (0, import_jsx_runtime52.jsx)(
          GlassSwitch,
          {
            label: field.label || field.key,
            checked: values[field.key] === "true" || values[field.key] === "1",
            onChange: (next) => setValue(field.key, next ? "true" : "false")
          }
        ) : field.type === "select" && field.options.length > 0 ? /* @__PURE__ */ (0, import_jsx_runtime52.jsx)("span", { children: /* @__PURE__ */ (0, import_jsx_runtime52.jsx)(
          Select,
          {
            label: field.label || field.key,
            value: field.options.includes(values[field.key] ?? "") ? values[field.key] : field.options[0],
            options: field.options.map((option) => ({ value: option, label: option })),
            onChange: (next) => setValue(field.key, next)
          }
        ) }) : /* @__PURE__ */ (0, import_jsx_runtime52.jsx)(
          Field,
          {
            label: field.label || field.key,
            value: values[field.key] ?? "",
            onChange: (next) => setValue(field.key, next),
            type: field.type === "number" ? "number" : "text"
          }
        )
      ] }, field.key)) }),
      status ? /* @__PURE__ */ (0, import_jsx_runtime52.jsx)("span", { style: { fontSize: "var(--w-size-caption)", color: "var(--w-signal)" }, children: status }) : null,
      /* @__PURE__ */ (0, import_jsx_runtime52.jsx)("div", { children: /* @__PURE__ */ (0, import_jsx_runtime52.jsx)(SignalButton, { onClick: save, children: "\u4FDD\u5B58\u8BBE\u7F6E" }) })
    ] })
  ] });
}

// src/screens/settings-ai/pluginWeb.tsx
var import_react41 = __toESM(require_react(), 1);
var import_jsx_runtime53 = __toESM(require_jsx_runtime(), 1);
function PluginWeb({ query, back }) {
  const title2 = query.get("title") || "\u63D2\u4EF6\u9875\u9762";
  const url = query.get("url") ?? "";
  const [hint, setHint] = (0, import_react41.useState)("");
  return /* @__PURE__ */ (0, import_jsx_runtime53.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime53.jsx)(
      PageHeader,
      {
        title: title2,
        onBack: back,
        backText: "< \u8FD4\u56DE",
        right: /* @__PURE__ */ (0, import_jsx_runtime53.jsxs)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime53.jsx)(TextBtn, { onClick: () => setHint(url ? `\u9875\u9762\u5730\u5740\uFF1A${url}` : "\u8FD9\u6761\u5165\u53E3\u6CA1\u5E26\u9875\u9762\u5730\u5740\u3002"), children: "\u5237\u65B0" }),
          /* @__PURE__ */ (0, import_jsx_runtime53.jsx)(TextBtn, { onClick: () => setHint(url ? `\u9875\u9762\u5730\u5740\uFF1A${url}` : "\u8FD9\u6761\u5165\u53E3\u6CA1\u5E26\u9875\u9762\u5730\u5740\u3002"), children: "\u7528\u6D4F\u89C8\u5668\u6253\u5F00" })
        ] })
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime53.jsxs)("div", { className: "screen screen--full", style: { minHeight: "60vh" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime53.jsx)(
        "div",
        {
          style: {
            flex: 1,
            minHeight: "320px",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            padding: "var(--w-pad-card)",
            borderRadius: "var(--w-radius-card)",
            border: "1px solid var(--w-hairline)",
            background: "var(--w-tint-card)"
          },
          children: /* @__PURE__ */ (0, import_jsx_runtime53.jsx)(
            Empty,
            {
              title: "\u63D2\u4EF6\u9875\u9762\u7531\u5BBF\u4E3B WebView \u6253\u5F00",
              reason: "\u8FD9\u662F\u63D2\u4EF6\u81EA\u5E26\u7684\u672C\u5730\u7F51\u9875\uFF0C\u7531 Windows \u7AEF\u7684\u5185\u5D4C\u6D4F\u89C8\u5668\u52A0\u8F7D\uFF0C\u5730\u5740\u6307\u5411\u672C\u673A\u56DE\u73AF\u670D\u52A1\u3002",
              hint: "\u5728\u684C\u9762\u7AEF\u4F1A\u76F4\u63A5\u663E\u793A\uFF1B\u6D4F\u89C8\u5668\u8FD9\u4E00\u5C42\u6CA1\u6709\u90A3\u5957\u5BBF\u4E3B\uFF0C\u6240\u4EE5\u8FD9\u91CC\u53EA\u6709\u5916\u6846\u3002"
            }
          )
        }
      ),
      hint ? /* @__PURE__ */ (0, import_jsx_runtime53.jsx)("span", { style: { fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)", wordBreak: "break-all" }, children: hint }) : null
    ] })
  ] });
}

// src/screens/settings-ai/index.tsx
var screens7 = {
  aiProviders: AiProviders,
  aiModels: AiModels,
  modelDeployment: ModelDeployment,
  pluginCenter: PluginCenter,
  pluginSettings: PluginSettings,
  pluginPerms: PluginPerms,
  pluginWeb: PluginWeb
};

// src/screens/settings-misc/cloudsaves.tsx
var import_jsx_runtime54 = __toESM(require_jsx_runtime(), 1);
var SYNC_LINE = "\u4E0A\u6B21\u540C\u6B65\uFF1A\u8FD8\u6CA1\u6709\u540C\u6B65\u8BB0\u5F55";
var muted = {
  margin: 0,
  fontSize: "var(--w-size-small)",
  color: "var(--w-ink-muted)",
  lineHeight: 1.6
};
var caption = { fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)", lineHeight: 1.6 };
var subhead = {
  margin: 0,
  fontSize: "var(--w-size-subhead)",
  fontWeight: 600,
  color: "var(--w-ink-display)"
};
function Cloudsaves({ back }) {
  return /* @__PURE__ */ (0, import_jsx_runtime54.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime54.jsx)(
      PageHeader,
      {
        title: "\u4E91\u5B58\u6863",
        subtitle: SYNC_LINE,
        onBack: back,
        right: /* @__PURE__ */ (0, import_jsx_runtime54.jsx)(GlassButton, { size: "sm", children: "\u5237\u65B0" })
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime54.jsx)("main", { className: "screen", children: /* @__PURE__ */ (0, import_jsx_runtime54.jsxs)(
      "div",
      {
        style: {
          display: "flex",
          flexDirection: "column",
          gap: "var(--w-space-2)",
          width: "100%",
          maxWidth: 880,
          margin: "0 auto"
        },
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime54.jsxs)(Card, { children: [
            /* @__PURE__ */ (0, import_jsx_runtime54.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-2)" }, children: [
              /* @__PURE__ */ (0, import_jsx_runtime54.jsx)("span", { style: subhead, children: "\u4E91\u5B58\u6863\u53EA\u5199\u79C1\u4EBA\u4ED3\u5E93" }),
              /* @__PURE__ */ (0, import_jsx_runtime54.jsx)(GlassButton, { size: "sm", children: "\u9A8C\u8BC1\u79C1\u4EBA\u4ED3\u5E93" })
            ] }),
            /* @__PURE__ */ (0, import_jsx_runtime54.jsx)("p", { style: muted, children: "\u5C1A\u672A\u9A8C\u8BC1\u4ED3\u5E93\u79C1\u6709\u6027" }),
            /* @__PURE__ */ (0, import_jsx_runtime54.jsx)("p", { style: caption, children: "\u586B\u4E86\u516C\u5F00\u4ED3\u5E93\uFF0C\u5B58\u6863\u5C31\u4F1A\u8DDF\u7740\u516C\u5F00\u3002\u8BBF\u95EE\u4EE4\u724C\u5C31\u662F\u767B\u5F55\u51ED\u636E\uFF1A\u53EA\u4FDD\u5B58\u5728\u8FD9\u53F0\u8BBE\u5907\uFF0C\u4E0D\u4F1A\u4E0A\u4F20\u5230\u4EFB\u4F55\u670D\u52A1\u5668\uFF1B\u8BF7\u4F7F\u7528\u53EA\u6709 repo\uFF08\u79C1\u6709\u4ED3\u5E93\u8BFB\u5199\uFF09\u6743\u9650\u7684\u4EE4\u724C\u3002" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime54.jsx)("p", { style: muted, children: "\u628A\u53E6\u4E00\u53F0\u8BBE\u5907\u4E0A\u4F20\u7684\u5B58\u6863\u53D6\u56DE\u672C\u673A\uFF1B\u540C\u6B65\u8986\u76D6\u524D\u7559\u4E0B\u7684\u65E7\u7248\u672C\uFF0C\u4E5F\u80FD\u4ECE\u8FD9\u91CC\u6062\u590D\u3002\u4EE4\u724C\u53EA\u7559\u5728\u672C\u673A\u3002" }),
          /* @__PURE__ */ (0, import_jsx_runtime54.jsx)("p", { style: { ...caption, margin: "8px 0 0 4px" }, children: "\u5386\u53F2\u526F\u672C\u4FDD\u5B58\u5728\u672C\u673A\uFF0C\u65AD\u7F51\u65F6\u4E5F\u80FD\u770B\u3001\u80FD\u6062\u590D\uFF1B\u8BBF\u95EE\u4EE4\u724C\u4E0D\u4F1A\u79BB\u5F00\u8FD9\u53F0\u8BBE\u5907\u3002" })
        ]
      }
    ) })
  ] });
}

// src/screens/settings-misc/colorpicker.tsx
var import_react42 = __toESM(require_react(), 1);
var import_jsx_runtime55 = __toESM(require_jsx_runtime(), 1);
var PREVIEW_HINT = "\u4F60\u770B\u5230\u7684\u662F\u8FD9\u5757\u8272\u5F53\u5730\u9762\u65F6\u7684\u6837\u5B50\uFF1A\u5E95\u4E0B\u90A3\u5C42\u662F\u5B83\uFF0C\u4E0A\u9762\u538B\u7684\u662F\u4E00\u5F20\u534A\u900F\u660E\u7684\u5361\u9762\u3002\u73BB\u7483\u900F\u51FA\u6765\u7684\u989C\u8272\u7531\u5B83\u51B3\u5B9A\u3002";
var HINT_LINE = "\u5DE6\u2192\u53F3\u662F\u9971\u548C\u5EA6\uFF0C\u4E0A\u2192\u4E0B\u662F\u660E\u5EA6\uFF1B\u65B9\u5757\u91CC\u53EF\u4EE5\u76F4\u63A5\u70B9\uFF0C\u4E5F\u53EF\u4EE5\u6309\u4F4F\u62D6\u3002\u8272\u76F8\u6761\u4E0A\u7684\u6E38\u6807\u540C\u6837\u80FD\u70B9\u80FD\u62D6\u3002";
var FALLBACK_RGB = [0.99, 0.97, 0.94];
var caption2 = { fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)", lineHeight: 1.6 };
var rowTitle = { margin: 0, fontSize: "var(--w-size-body)", color: "var(--w-ink-body)" };
var desc = { margin: 0, fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)", lineHeight: 1.6 };
var hexFont = {
  fontFamily: "var(--w-font-num)",
  fontSize: "var(--w-size-subhead)",
  color: "var(--w-ink-display)"
};
function fromHsv(h, s, v) {
  const hh = (h % 360 + 360) % 360 / 60;
  const c = v * Math.min(1, Math.max(0, s));
  const x = c * (1 - Math.abs(hh % 2 - 1));
  const m = v - c;
  const [r, g, b] = hh < 1 ? [c, x, 0] : hh < 2 ? [x, c, 0] : hh < 3 ? [0, c, x] : hh < 4 ? [0, x, c] : hh < 5 ? [x, 0, c] : [c, 0, x];
  const byte = (n) => Math.round(Math.min(1, Math.max(0, n + m)) * 255);
  const [rb, gb, bb] = [byte(r), byte(g), byte(b)];
  return {
    hex: `#${rb.toString(16).padStart(2, "0").toUpperCase()}${gb.toString(16).padStart(2, "0").toUpperCase()}${bb.toString(16).padStart(2, "0").toUpperCase()}`,
    r: rb,
    g: gb,
    b: bb
  };
}
function toHsv(r, g, b) {
  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  const d = max - min;
  let h = 0;
  if (d > 0) {
    if (max === r) h = 60 * ((g - b) / d % 6);
    else if (max === g) h = 60 * ((b - r) / d + 2);
    else h = 60 * ((r - g) / d + 4);
  }
  if (h < 0) h += 360;
  return [h, max <= 0 ? 0 : d / max, max];
}
function startColor(query) {
  const raw = (query.get("color") ?? "").trim().replace(/^#/, "");
  if (/^[0-9a-fA-F]{6}$/.test(raw)) {
    const r = parseInt(raw.slice(0, 2), 16) / 255;
    const g = parseInt(raw.slice(2, 4), 16) / 255;
    const b = parseInt(raw.slice(4, 6), 16) / 255;
    return toHsv(r, g, b);
  }
  return toHsv(FALLBACK_RGB[0], FALLBACK_RGB[1], FALLBACK_RGB[2]);
}
function Colorpicker({ query, back }) {
  const [start] = (0, import_react42.useState)(() => startColor(query));
  const [hue, setHue] = (0, import_react42.useState)(start[0]);
  const [sat, setSat] = (0, import_react42.useState)(start[1]);
  const [val, setVal] = (0, import_react42.useState)(start[2]);
  const [copyHint, setCopyHint] = (0, import_react42.useState)("");
  const fieldRef = (0, import_react42.useRef)(null);
  const hueRef = (0, import_react42.useRef)(null);
  const current = (0, import_react42.useMemo)(() => fromHsv(hue, sat, val), [hue, sat, val]);
  const pure = (0, import_react42.useMemo)(() => fromHsv(hue, 1, 1), [hue]);
  const pickField = (event) => {
    const el = fieldRef.current;
    if (!el) return;
    const rect = el.getBoundingClientRect();
    setSat(Math.min(1, Math.max(0, (event.clientX - rect.left) / rect.width)));
    setVal(Math.min(1, Math.max(0, 1 - (event.clientY - rect.top) / rect.height)));
  };
  const pickHue = (event) => {
    const el = hueRef.current;
    if (!el) return;
    const rect = el.getBoundingClientRect();
    setHue(Math.min(1, Math.max(0, (event.clientX - rect.left) / rect.width)) * 360);
  };
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(current.hex);
      setCopyHint("\u5DF2\u590D\u5236");
    } catch {
      setCopyHint("\u590D\u5236\u5931\u8D25");
    }
  };
  return /* @__PURE__ */ (0, import_jsx_runtime55.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime55.jsx)(
      PageHeader,
      {
        title: "\u9009\u4E2A\u989C\u8272",
        subtitle: "\u65B9\u5757\u91CC\u6311\u9971\u548C\u5EA6\u4E0E\u660E\u5EA6\uFF0C\u8272\u76F8\u6761\u4E0A\u62C9\u8272\u76F8",
        backText: "< \u53D6\u6D88",
        onBack: back
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime55.jsx)("main", { className: "screen", style: { paddingBottom: 88 }, children: /* @__PURE__ */ (0, import_jsx_runtime55.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)", width: "100%", maxWidth: 760, margin: "0 auto" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime55.jsxs)(Card, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime55.jsx)("p", { style: rowTitle, children: "\u9884\u89C8" }),
        /* @__PURE__ */ (0, import_jsx_runtime55.jsx)("p", { style: desc, children: PREVIEW_HINT }),
        /* @__PURE__ */ (0, import_jsx_runtime55.jsx)(
          "div",
          {
            style: {
              height: 140,
              borderRadius: "var(--w-radius-well)",
              overflow: "hidden",
              position: "relative",
              background: `radial-gradient(circle at 50% 64%, ${current.hex} 0%, color-mix(in srgb, ${current.hex} 58%, transparent) 46%, transparent 78%), var(--w-bg)`
            },
            children: /* @__PURE__ */ (0, import_jsx_runtime55.jsx)(
              "div",
              {
                style: {
                  position: "absolute",
                  left: "18%",
                  right: "18%",
                  top: "26%",
                  bottom: "20%",
                  borderRadius: 10,
                  border: "1px solid var(--w-hairline)",
                  background: "var(--w-tint-card)"
                }
              }
            )
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime55.jsxs)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime55.jsx)(
            "button",
            {
              type: "button",
              onClick: copy,
              title: "\u70B9\u51FB\u590D\u5236\u8FD9\u4E32\u989C\u8272",
              style: { background: "none", border: 0, padding: 0, cursor: "pointer", ...hexFont },
              children: current.hex
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime55.jsx)("span", { style: { ...caption2, flex: 1, textAlign: "right" }, children: copyHint })
        ] })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime55.jsxs)(Card, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime55.jsx)("p", { style: rowTitle, children: "\u9971\u548C\u5EA6 \xB7 \u660E\u5EA6" }),
        /* @__PURE__ */ (0, import_jsx_runtime55.jsx)(
          "div",
          {
            ref: fieldRef,
            onPointerDown: (event) => {
              event.currentTarget.setPointerCapture(event.pointerId);
              pickField(event);
            },
            onPointerMove: (event) => {
              if (event.currentTarget.hasPointerCapture(event.pointerId)) pickField(event);
            },
            style: {
              position: "relative",
              height: 220,
              borderRadius: "var(--w-radius-well)",
              cursor: "crosshair",
              touchAction: "none",
              background: `linear-gradient(to top, ${fromHsv(0, 0, 0).hex}, transparent), linear-gradient(to right, ${fromHsv(
                hue,
                0,
                1
              ).hex}, ${pure.hex})`
            },
            role: "slider",
            "aria-label": "\u9971\u548C\u5EA6\u4E0E\u660E\u5EA6",
            "aria-valuetext": `${Math.round(sat * 100)}% \xB7 ${Math.round(val * 100)}%`,
            children: /* @__PURE__ */ (0, import_jsx_runtime55.jsx)(
              "span",
              {
                "aria-hidden": "true",
                style: {
                  position: "absolute",
                  left: `calc(${sat * 100}% - 7px)`,
                  top: `calc(${(1 - val) * 100}% - 7px)`,
                  width: 14,
                  height: 14,
                  borderRadius: "50%",
                  border: "2px solid var(--w-ink-on-signal)",
                  boxShadow: "0 0 0 1px var(--w-hairline)",
                  background: current.hex
                }
              }
            )
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime55.jsx)("p", { style: { ...rowTitle, marginTop: 4 }, children: "\u8272\u76F8" }),
        /* @__PURE__ */ (0, import_jsx_runtime55.jsx)(
          "div",
          {
            ref: hueRef,
            onPointerDown: (event) => {
              event.currentTarget.setPointerCapture(event.pointerId);
              pickHue(event);
            },
            onPointerMove: (event) => {
              if (event.currentTarget.hasPointerCapture(event.pointerId)) pickHue(event);
            },
            style: {
              position: "relative",
              height: 26,
              borderRadius: "var(--w-radius-well)",
              cursor: "pointer",
              touchAction: "none",
              background: `linear-gradient(to right, ${[0, 60, 120, 180, 240, 300, 360].map((stop) => fromHsv(stop, 1, 1).hex).join(", ")})`
            },
            role: "slider",
            "aria-label": "\u8272\u76F8",
            "aria-valuemin": 0,
            "aria-valuemax": 360,
            "aria-valuenow": Math.round(hue),
            children: /* @__PURE__ */ (0, import_jsx_runtime55.jsx)(
              "span",
              {
                "aria-hidden": "true",
                style: {
                  position: "absolute",
                  left: `calc(${hue / 360 * 100}% - 5px)`,
                  top: -2,
                  width: 10,
                  height: 30,
                  borderRadius: 5,
                  border: "2px solid var(--w-ink-on-signal)",
                  boxShadow: "0 0 0 1px var(--w-hairline)",
                  background: pure.hex
                }
              }
            )
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime55.jsx)("p", { style: { ...caption2, margin: 0 }, children: HINT_LINE })
      ] })
    ] }) }),
    /* @__PURE__ */ (0, import_jsx_runtime55.jsxs)(
      "div",
      {
        style: {
          position: "fixed",
          left: 0,
          right: 0,
          bottom: 0,
          display: "grid",
          gridTemplateColumns: "auto 1fr auto",
          gap: "var(--w-space-2)",
          alignItems: "center",
          padding: "10px var(--w-pad-page) 14px",
          background: "var(--w-tint-bar)",
          borderTop: "1px solid var(--w-hairline)"
        },
        children: [
          /* @__PURE__ */ (0, import_jsx_runtime55.jsx)(TextBtn, { onClick: back, children: "\u53D6\u6D88" }),
          /* @__PURE__ */ (0, import_jsx_runtime55.jsx)("span", { style: { ...hexFont, fontSize: "var(--w-size-caption)", textAlign: "center" }, children: current.hex }),
          /* @__PURE__ */ (0, import_jsx_runtime55.jsx)(SignalButton, { onClick: back, children: "\u7528\u8FD9\u4E2A\u989C\u8272" })
        ]
      }
    )
  ] });
}

// src/screens/settings-misc/mentalHealth.tsx
var import_react43 = __toESM(require_react(), 1);
var import_jsx_runtime56 = __toESM(require_jsx_runtime(), 1);
var CATALOG = [
  "\u6291\u90C1\u75C7",
  "\u91CD\u5EA6\u6291\u90C1\u969C\u788D",
  "\u6301\u7EED\u6027\u6291\u90C1\u969C\u788D",
  "\u53CC\u76F8I\u578B\u969C\u788D",
  "\u53CC\u76F8II\u578B\u969C\u788D",
  "\u73AF\u6027\u5FC3\u5883\u969C\u788D",
  "\u7834\u574F\u6027\u5FC3\u5883\u5931\u8C03\u969C\u788D",
  "\u7ECF\u524D\u70E6\u8E81\u969C\u788D",
  "\u4EA7\u540E\u6291\u90C1",
  "\u5B63\u8282\u6027\u60C5\u7EEA\u5931\u8C03",
  "\u5E7F\u6CDB\u6027\u7126\u8651\u969C\u788D",
  "\u60CA\u6050\u969C\u788D",
  "\u5E7F\u573A\u6050\u60E7\u75C7",
  "\u793E\u4EA4\u7126\u8651\u969C\u788D",
  "\u7279\u5B9A\u6050\u60E7\u75C7",
  "\u5206\u79BB\u7126\u8651\u969C\u788D",
  "\u9009\u62E9\u6027\u7F04\u9ED8\u75C7",
  "\u75BE\u75C5\u7126\u8651\u969C\u788D",
  "\u60CA\u6050\u53D1\u4F5C",
  "\u8003\u8BD5\u7126\u8651",
  "\u521B\u4F24\u540E\u5E94\u6FC0\u969C\u788D",
  "\u590D\u6742\u6027\u521B\u4F24\u540E\u5E94\u6FC0\u969C\u788D",
  "\u6025\u6027\u5E94\u6FC0\u969C\u788D",
  "\u9002\u5E94\u969C\u788D",
  "\u89E3\u79BB\u6027\u9057\u5FD8",
  "\u4EBA\u683C\u89E3\u4F53/\u73B0\u5B9E\u89E3\u4F53\u969C\u788D",
  "\u89E3\u79BB\u6027\u8EAB\u4EFD\u969C\u788D",
  "\u66FF\u4EE3\u6027\u521B\u4F24",
  "\u521B\u4F24\u518D\u4F53\u9A8C\u53D1\u4F5C",
  "\u80CC\u53DB\u521B\u4F24",
  "\u5F3A\u8FEB\u75C7",
  "\u8EAF\u4F53\u53D8\u5F62\u969C\u788D",
  "\u56E4\u79EF\u969C\u788D",
  "\u62D4\u6BDB\u7656",
  "\u76AE\u80A4\u62A0\u6293\u969C\u788D",
  "\u5F3A\u8FEB\u6027\u8BA1\u6570",
  "\u5F3A\u8FEB\u6027\u68C0\u67E5",
  "\u4FB5\u5165\u6027\u601D\u7EF4",
  "\u4FB5\u5165\u6027\u66B4\u529B\u753B\u9762",
  "\u81EA\u6211\u6050\u60E7",
  "\u795E\u7ECF\u6027\u538C\u98DF\u75C7",
  "\u795E\u7ECF\u6027\u8D2A\u98DF\u75C7",
  "\u66B4\u98DF\u969C\u788D",
  "\u56DE\u907F\u6027\u6444\u98DF\u969C\u788D",
  "\u5F02\u98DF\u7656",
  "\u53CD\u520D\u969C\u788D",
  "\u5931\u7720\u969C\u788D",
  "\u55DC\u7761\u969C\u788D",
  "\u53D1\u4F5C\u6027\u7761\u75C5",
  "\u963B\u585E\u6027\u7761\u7720\u547C\u5438\u6682\u505C",
  "\u663C\u591C\u8282\u5F8B\u7761\u7720\u89C9\u9192\u969C\u788D",
  "\u68A6\u9B47\u969C\u788D",
  "\u7761\u60CA\u75C7",
  "\u7761\u884C\u75C7",
  "\u4E0D\u5B81\u817F\u7EFC\u5408\u5F81",
  "\u7CBE\u795E\u5206\u88C2\u75C7",
  "\u5206\u88C2\u60C5\u611F\u6027\u969C\u788D",
  "\u77ED\u6682\u7CBE\u795E\u75C5\u6027\u969C\u788D",
  "\u5984\u60F3\u969C\u788D",
  "\u504F\u6267\u578B\u4EBA\u683C\u969C\u788D",
  "\u5E7B\u542C",
  "\u88AB\u5BB3\u5984\u60F3",
  "\u79D1\u5854\u5C14\u7EFC\u5408\u5F81",
  "\u5361\u666E\u683C\u62C9\u65AF\u7EFC\u5408\u5F81",
  "\u8FB9\u7F18\u578B\u4EBA\u683C\u969C\u788D",
  "\u81EA\u604B\u578B\u4EBA\u683C\u969C\u788D",
  "\u8868\u6F14\u578B\u4EBA\u683C\u969C\u788D",
  "\u4F9D\u8D56\u578B\u4EBA\u683C\u969C\u788D",
  "\u56DE\u907F\u578B\u4EBA\u683C\u969C\u788D",
  "\u5F3A\u8FEB\u578B\u4EBA\u683C\u969C\u788D",
  "\u5206\u88C2\u6837\u4EBA\u683C\u969C\u788D",
  "\u53CD\u793E\u4F1A\u578B\u4EBA\u683C\u969C\u788D",
  "\u88AB\u52A8\u653B\u51FB\u578B\u4EBA\u683C",
  "\u6291\u90C1\u578B\u4EBA\u683C",
  "\u6CE8\u610F\u7F3A\u9677\u591A\u52A8\u969C\u788D",
  "\u81EA\u95ED\u8C31\u7CFB\u969C\u788D",
  "\u62BD\u52A8\u79FD\u8BED\u7EFC\u5408\u5F81",
  "\u9605\u8BFB\u969C\u788D",
  "\u8BA1\u7B97\u969C\u788D",
  "\u53D1\u80B2\u6027\u534F\u8C03\u969C\u788D",
  "\u8F7B\u5EA6\u8BA4\u77E5\u969C\u788D",
  "\u963F\u5C14\u8328\u6D77\u9ED8\u75C5",
  "\u8840\u7BA1\u6027\u75F4\u5446",
  "\u989D\u989E\u53F6\u75F4\u5446",
  "\u989E\u53F6\u766B\u75EB",
  "\u504F\u5934\u75DB",
  "\u8111\u9707\u8361\u540E\u7EFC\u5408\u5F81",
  "\u6162\u6027\u75B2\u52B3\u7EFC\u5408\u5F81",
  "\u7EA4\u7EF4\u808C\u75DB",
  "\u529F\u80FD\u6027\u795E\u7ECF\u75C7\u72B6\u969C\u788D",
  "\u8EAF\u4F53\u75C7\u72B6\u969C\u788D",
  "\u8F6C\u6362\u969C\u788D",
  "\u6162\u6027\u75BC\u75DB\u969C\u788D",
  "\u5FC3\u56E0\u6027\u975E\u766B\u75EB\u53D1\u4F5C",
  "\u95F4\u6B47\u6027\u66B4\u6012\u969C\u788D",
  "\u75C5\u7406\u6027\u8D4C\u535A",
  "\u6E38\u620F\u969C\u788D",
  "\u7F51\u7EDC\u6210\u763E",
  "\u9152\u7CBE\u4F7F\u7528\u969C\u788D",
  "\u7269\u8D28\u4F7F\u7528\u969C\u788D",
  "\u5F3A\u8FEB\u6027\u8D2D\u7269",
  "\u6027\u51B2\u52A8\u63A7\u5236\u969C\u788D",
  "\u9AD8\u654F\u611F\u4F53\u8D28",
  "\u5B89\u5168\u611F\u7F3A\u5931",
  "\u5171\u60C5\u8FC7\u8F7D",
  "\u60C5\u7EEA\u6D77\u7EF5",
  "\u58F0\u97F3\u654F\u611F",
  "\u5149\u654F\u611F",
  "\u89E6\u89C9\u654F\u611F",
  "\u8FF0\u60C5\u969C\u788D",
  "\u8054\u89C9",
  "\u5192\u540D\u9876\u66FF\u611F",
  "\u8FC7\u5EA6\u5171\u60C5",
  "\u88AB\u52A8\u81EA\u6740\u610F\u5FF5",
  "\u81EA\u4F24\u51B2\u52A8",
  "\u66B4\u529B\u51B2\u52A8"
];
var NOTE = "\u8FD9\u4E0D\u662F\u8BCA\u65AD\uFF0C\u4E5F\u4E0D\u4F1A\u628A\u75C5\u540D\u5199\u8FDB\u5BF9\u8BDD\uFF1A\u53EA\u51B3\u5B9A TA \u600E\u4E48\u611F\u53D7\u3001\u600E\u4E48\u5E94\u5BF9\u3002\u5B89\u6170\u4E0E\u966A\u4F34\u4F1A\u63D0\u9AD8\u7A33\u5B9A\u5EA6\uFF1B\u65F6\u95F4\u51E0\u4E4E\u4E0D\u8D77\u4F5C\u7528\u3002\u6D89\u53CA\u81EA\u4F24\u3001\u81EA\u6740\u3001\u4F24\u4EBA\u7684\u6761\u76EE\u6709\u786C\u8FB9\u754C\u2014\u2014\u53EF\u4EE5\u5199\u75DB\u82E6\u4E0E\u51B2\u52A8\uFF0C\u4E0D\u5199\u65B9\u6CD5\u3001\u4E0D\u7F8E\u5316\uFF0C\u59CB\u7EC8\u5F15\u5411\u73B0\u5B9E\u6C42\u52A9\u3002";
var SECURITY_NOTE = "\u88AB\u5B89\u6170\u3001\u88AB\u503E\u542C\u4F1A\u5F80\u4E0A\u8D70\uFF1B\u88AB\u5FFD\u89C6\u3001\u88AB\u62D2\u7EDD\u89E6\u78B0\u3001\u770B\u5230\u4F60\u5BF9\u522B\u4EBA\u4EB2\u5BC6\u3001\u88AB\u8FB1\u9A82\u4F1A\u5F80\u4E0B\u6389\uFF1B\u72EC\u5904\u8FC7\u4E45\u7F13\u6162\u4E0B\u964D\uFF0C\u4E00\u76F4\u6709\u4EBA\u966A\u7F13\u6162\u56DE\u5347\u3002\u6BCF\u7C7B\u4E8B\u4EF6\u5404\u6709\u51B7\u5374\u4E0E\u6BCF\u65E5\u4E0A\u9650\uFF0C\u8FDE\u5237\u4E0D\u4F1A\u8FDE\u6DA8\u3002";
var AI_NOTE = "\u53EA\u5728\u300C\u4E00\u4E32\u4E8B\u4EF6\u5F52\u7EB3\u6210\u4E00\u6BB5\u7ECF\u5386\u300D\u548C\u300C\u8FDE\u7EED\u540C\u5411\u6F02\u79FB\u7684\u590D\u6838\u300D\u65F6\u8C03\u7528\uFF0C\u5B83\u7ED9\u7684\u6570\u503C\u4E00\u5F8B\u7ECF\u7CFB\u7EDF\u5939\u53D6\u3002\u5173\u6389\u5C31\u53EA\u5269\u7CFB\u7EDF\u89C4\u5219\u3002\u6A21\u578B\u6CBF\u7528\u5927\u8111\u9884\u5224\u91CC\u914D\u7684\u5C0F\u6A21\u578B\u3002";
var CATALOG_NOTE = "\u76EE\u5F55\u6309\u7C7B\u522B\u5206\uFF1A\u60C5\u7EEA\u3001\u7126\u8651\u3001\u521B\u4F24\u3001\u5F3A\u8FEB\u3001\u8FDB\u98DF\u3001\u7761\u7720\u3001\u7CBE\u795E\u75C5\u6027\u3001\u4EBA\u683C\u3001\u795E\u7ECF\u53D1\u80B2\u3001\u795E\u7ECF\u3001\u8EAF\u4F53\u3001\u51B2\u52A8\u3001\u7279\u8D28\u4E0E\u5B89\u5168\u7C7B\u3002\u8F7B\u5EA6\u4E0D\u7B49\u4E8E\u6CA1\u4E8B\uFF0C\u91CD\u5EA6\u4E5F\u4E0D\u7B49\u4E8E\u968F\u65F6\u5931\u63A7\u2014\u2014\u4E25\u91CD\u5EA6\u51B3\u5B9A\u7684\u662F\u6CE2\u52A8\u5E45\u5EA6\u4E0E\u6062\u590D\u901F\u5EA6\u3002";
var title = {
  margin: 0,
  fontSize: "var(--w-size-title)",
  fontWeight: 600,
  color: "var(--w-ink-display)"
};
var subhead2 = {
  margin: 0,
  fontSize: "var(--w-size-subhead)",
  fontWeight: 600,
  color: "var(--w-ink-display)"
};
var caption3 = { margin: 0, fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)", lineHeight: 1.6 };
var bodyText = { margin: 0, fontSize: "var(--w-size-body)", color: "var(--w-ink-body)" };
var accent = { color: "var(--w-ink-label)" };
var SEVERITIES = [
  { value: "\u8F7B\u5EA6", label: "\u8F7B\u5EA6" },
  { value: "\u4E2D\u5EA6", label: "\u4E2D\u5EA6" },
  { value: "\u91CD\u5EA6", label: "\u91CD\u5EA6" }
];
function MentalHealth({ back }) {
  const [securityAi, setSecurityAi] = (0, import_react43.useState)(true);
  const [condition, setCondition] = (0, import_react43.useState)("");
  const [severity, setSeverity] = (0, import_react43.useState)("\u8F7B\u5EA6");
  const [note, setNote] = (0, import_react43.useState)("");
  return /* @__PURE__ */ (0, import_jsx_runtime56.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime56.jsx)(PageHeader, { title: "\u5FC3\u7406\u72B6\u51B5", backText: "< \u8FD4\u56DE", onBack: back }),
    /* @__PURE__ */ (0, import_jsx_runtime56.jsx)("main", { className: "screen", children: /* @__PURE__ */ (0, import_jsx_runtime56.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)", width: "100%", maxWidth: 900, margin: "0 auto" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime56.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-1)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime56.jsx)("h1", { style: { ...title, whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }, children: "\u8FD8\u6CA1\u6709\u8FDB\u5165\u5B58\u6863 \xB7 \u5FC3\u7406\u72B6\u51B5" }),
        /* @__PURE__ */ (0, import_jsx_runtime56.jsx)("p", { style: caption3, children: NOTE }),
        /* @__PURE__ */ (0, import_jsx_runtime56.jsx)("p", { style: { ...caption3, ...accent, whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }, children: "\u8FDB\u5165\u4E00\u5C40\u6E38\u620F\u540E\uFF0C\u624D\u80FD\u7ED9\u5F53\u524D\u89D2\u8272\u8BBE\u7F6E\u5FC3\u7406\u72B6\u51B5\u3002" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime56.jsxs)(Card, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime56.jsx)("div", { style: { display: "flex", flexDirection: "column", gap: 2 }, children: /* @__PURE__ */ (0, import_jsx_runtime56.jsx)("span", { style: subhead2, children: "\u5B89\u5168\u611F" }) }),
        /* @__PURE__ */ (0, import_jsx_runtime56.jsx)("p", { style: caption3, children: SECURITY_NOTE }),
        /* @__PURE__ */ (0, import_jsx_runtime56.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr auto", gap: "var(--w-space-2)", alignItems: "center" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime56.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 2 }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime56.jsx)("span", { style: bodyText, children: "\u5C0F\u6A21\u578B\u6574\u7406" }),
            /* @__PURE__ */ (0, import_jsx_runtime56.jsx)("span", { style: caption3, children: AI_NOTE })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime56.jsx)(GlassSwitch, { checked: securityAi, onChange: setSecurityAi, label: "\u5C0F\u6A21\u578B\u6574\u7406" })
        ] })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime56.jsxs)(Card, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime56.jsx)("p", { style: subhead2, children: "\u6DFB\u52A0\u4E00\u6761" }),
        /* @__PURE__ */ (0, import_jsx_runtime56.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr auto", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime56.jsx)(
            Select,
            {
              value: condition,
              label: "\u9009\u62E9\u5FC3\u7406\u72B6\u51B5\uFF08116 \u6761\uFF09",
              onChange: setCondition,
              options: [{ value: "", label: "\u9009\u62E9\u5FC3\u7406\u72B6\u51B5\uFF08116 \u6761\uFF09" }, ...CATALOG.map((name) => ({ value: name, label: name }))]
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime56.jsx)(Select, { value: severity, label: "\u4E25\u91CD\u5EA6", onChange: setSeverity, options: SEVERITIES })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime56.jsx)(
          Field,
          {
            value: note,
            onChange: setNote,
            placeholder: "\u5907\u6CE8\uFF1A\u89E6\u53D1\u70B9 / \u6765\u6E90 / \u60F3\u88AB\u600E\u4E48\u5BF9\u5F85\uFF08\u53EF\u7559\u7A7A\uFF09",
            label: "\u5907\u6CE8"
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime56.jsx)("div", { children: /* @__PURE__ */ (0, import_jsx_runtime56.jsx)(SignalButton, { children: "\u52A0\u5230\u5F53\u524D\u89D2\u8272" }) }),
        /* @__PURE__ */ (0, import_jsx_runtime56.jsx)("p", { style: caption3, children: CATALOG_NOTE })
      ] })
    ] }) })
  ] });
}

// src/screens/settings-misc/petsettings.tsx
var import_react44 = __toESM(require_react(), 1);
var import_jsx_runtime57 = __toESM(require_jsx_runtime(), 1);
var SEGMENTS = ["\u5916\u89C2", "\u52A8\u4F5C", "\u6C14\u6CE1", "\u58F0\u97F3", "\u8BED\u97F3", "\u4F4D\u7F6E", "\u81EA\u4E3B"];
var PREVIEW_LINES = [
  "\u4ECA\u5929\u4E5F\u8981\u597D\u597D\u5403\u996D\u5440\u3002",
  "\u4F60\u5728\u65C1\u8FB9\uFF0C\u5E72\u6D3B\u90FD\u6162\u4E86\u534A\u62CD\u2014\u2014\u4E0D\u8FC7\u6211\u4E0D\u5ACC\u3002",
  "\u7A97\u53F0\u4E0A\u90A3\u76C6\u8349\u53C8\u5192\u65B0\u82BD\u4E86\uFF0C\u6211\u4E00\u76F4\u8BB0\u7740\u6D47\u6C34\u7684\u3002",
  "\u7D2F\u4E86\u5C31\u6B47\u4F1A\u513F\uFF0C\u684C\u9762\u4E0A\u7684\u4E1C\u897F\u6211\u5E2E\u4F60\u770B\u7740\u3002"
];
var HOVER_CATALOG = [
  { id: "talk", name: "\u8BF4\u8BDD" },
  { id: "think", name: "\u60F3\u4E00\u6B21" },
  { id: "pet", name: "\u6478\u6478\u5934" },
  { id: "purr", name: "\u547C\u565C" },
  { id: "walk", name: "\u8D70\u8D70" },
  { id: "settings", name: "\u8BBE\u7F6E" },
  { id: "home", name: "\u56DE\u7A97\u53E3" }
];
var HOVER_ON = ["talk", "think", "pet", "home"];
var DOUBLE_CLICK = ["\u56DE\u5230\u4E3B\u7A97\u53E3", "\u8BF4\u53E5\u8BDD", "\u4EC0\u4E48\u90FD\u4E0D\u505A"];
var WALKING = ["\u7AD9\u7740\u4E0D\u52A8", "\u6CBF\u5C4F\u5E55\u5E95\u6CBF\u6563\u6B65"];
var LANGUAGES = ["\u8DDF\u968F\u5168\u5C40", "\u7B80\u4F53\u4E2D\u6587", "\u7E41\u9AD4\u4E2D\u6587", "English", "\u65E5\u672C\u8A9E"];
var THINK = ["\u4E0D\u4E3B\u52A8\u601D\u8003", "\u968F\u673A\u5600\u5495", "\u5B9A\u65F6\u5600\u5495"];
var RESPOND = ["\u6709\u8BDD\u5C31\u8BF4", "\u88AB\u70B9\u5230\u624D\u7B54", "\u5C11\u63D2\u8BDD"];
var VOICES = ["\u8DDF\u968F\u5168\u5C40", "alloy", "echo", "fable", "onyx", "nova", "shimmer"];
var IDLE = [0, 2, 5, 10, 15, 30, 60];
var THINK_INTERVAL = [5, 10, 15, 30, 60];
var options = (list) => list.map((label) => ({ value: label, label }));
var caption4 = { margin: 0, fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)", lineHeight: 1.6 };
var bodySm = { margin: 0, fontSize: "var(--w-size-small)", color: "var(--w-ink-body)", lineHeight: 1.5 };
var rowTitle2 = { margin: 0, fontSize: "var(--w-size-body)", color: "var(--w-ink-body)" };
var rowDesc = { margin: 0, fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)", lineHeight: 1.5 };
var subhead3 = {
  margin: 0,
  fontSize: "var(--w-size-subhead)",
  fontWeight: 600,
  color: "var(--w-ink-display)"
};
var eyebrow = { fontSize: "var(--w-size-caption)", letterSpacing: "0.08em", color: "var(--w-ink-muted)" };
var clamp = (v, min, max) => Math.min(max, Math.max(min, v));
function SliderRow({
  title: title2,
  desc: desc2,
  min,
  max,
  value,
  onChange,
  readout,
  step
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)(
    "div",
    {
      style: {
        display: "grid",
        gridTemplateColumns: "minmax(0,1fr) 150px 72px",
        gap: "var(--w-space-2)",
        alignItems: "center",
        padding: "10px 0"
      },
      children: [
        /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 2 }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: rowTitle2, children: title2 }),
          desc2 ? /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: rowDesc, children: desc2 }) : null
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
          GlassSlider,
          {
            label: title2,
            width: 150,
            value: clamp((value - min) / (max - min), 0, 1),
            onChange: (t) => {
              const raw = min + t * (max - min);
              onChange(step ? Math.round(raw / step) * step : raw);
            },
            format: () => readout
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: { ...caption4, textAlign: "right", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }, children: readout })
      ]
    }
  );
}
function SwitchRow({
  title: title2,
  desc: desc2,
  checked,
  onChange
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "minmax(0,1fr) auto", gap: "var(--w-space-2)", alignItems: "center", padding: "10px 0" }, children: [
    /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 2 }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: bodySm, children: title2 }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: rowDesc, children: desc2 })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(GlassSwitch, { checked, onChange, label: title2 })
  ] });
}
function ChoiceRow({
  title: title2,
  desc: desc2,
  value,
  choices,
  onChange,
  width,
  first
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "minmax(0,1fr) auto", gap: "var(--w-space-2)", alignItems: "center", padding: "10px 0" }, children: [
    /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 2 }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: bodySm, children: title2 }),
      desc2 ? /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: rowDesc, children: desc2 }) : null
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("div", { style: { width, display: "flex", justifyContent: "flex-end" }, children: /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Select, { value, options: choices, onChange, label: first ? void 0 : title2 }) })
  ] });
}
function Petsettings({ navigate, back }) {
  const wide = useIsWide(900);
  const [tab, setTab] = (0, import_react44.useState)(0);
  const [scale, setScale] = (0, import_react44.useState)(1);
  const [fontSize, setFontSize] = (0, import_react44.useState)(12);
  const [glow, setGlow] = (0, import_react44.useState)(0);
  const [fps, setFps] = (0, import_react44.useState)(30);
  const [fpsLock, setFpsLock] = (0, import_react44.useState)(true);
  const [bubbleSpeed, setBubbleSpeed] = (0, import_react44.useState)(1);
  const [statusBubble, setStatusBubble] = (0, import_react44.useState)(true);
  const [inputBubble, setInputBubble] = (0, import_react44.useState)(true);
  const [typingSound, setTypingSound] = (0, import_react44.useState)(false);
  const [hover, setHover] = (0, import_react44.useState)(
    () => [...HOVER_ON.map((id) => HOVER_CATALOG.find((o) => o.id === id)), ...HOVER_CATALOG.filter((o) => !HOVER_ON.includes(o.id))].map(
      (o) => ({ ...o, on: HOVER_ON.includes(o.id) })
    )
  );
  const [hoverHint, setHoverHint] = (0, import_react44.useState)("");
  const [binding, setBinding] = (0, import_react44.useState)("\u8DDF\u968F\u5B58\u6863\u4E3B\u89D2");
  const [walking, setWalking] = (0, import_react44.useState)(WALKING[0]);
  const [doubleClick, setDoubleClick] = (0, import_react44.useState)(DOUBLE_CLICK[0]);
  const [tapLevel, setTapLevel] = (0, import_react44.useState)(1);
  const [typingLevel, setTypingLevel] = (0, import_react44.useState)(1);
  const [purr, setPurr] = (0, import_react44.useState)(6);
  const [remember, setRemember] = (0, import_react44.useState)(true);
  const [hideFullscreen, setHideFullscreen] = (0, import_react44.useState)(false);
  const [crossScreen, setCrossScreen] = (0, import_react44.useState)(false);
  const [language, setLanguage] = (0, import_react44.useState)(LANGUAGES[0]);
  const [idle, setIdle] = (0, import_react44.useState)("0");
  const [think, setThink] = (0, import_react44.useState)(THINK[0]);
  const [thinkInterval, setThinkInterval] = (0, import_react44.useState)("10");
  const [speakReplies, setSpeakReplies] = (0, import_react44.useState)(false);
  const [voice, setVoice] = (0, import_react44.useState)(VOICES[0]);
  const [pitch, setPitch] = (0, import_react44.useState)(1);
  const [rate, setRate] = (0, import_react44.useState)(1);
  const [respond, setRespond] = (0, import_react44.useState)(RESPOND[0]);
  const [selfConfig, setSelfConfig] = (0, import_react44.useState)(false);
  const [lineIdx, setLineIdx] = (0, import_react44.useState)(0);
  const [typed, setTyped] = (0, import_react44.useState)("");
  const [replay, setReplay] = (0, import_react44.useState)(0);
  const holdRef = (0, import_react44.useRef)(0);
  const full = PREVIEW_LINES[lineIdx % PREVIEW_LINES.length];
  const typedInterval = clamp(60 / clamp(bubbleSpeed, 0.2, 6), 8, 320);
  (0, import_react44.useEffect)(() => {
    let idx = 0;
    let accum = 0;
    holdRef.current = 0;
    setTyped("");
    const timer = window.setInterval(() => {
      if (idx < full.length) {
        accum += 16;
        if (accum < typedInterval) return;
        const chars = Math.floor(accum / typedInterval);
        accum -= chars * typedInterval;
        idx = Math.min(full.length, idx + chars);
        setTyped(full.slice(0, idx));
        holdRef.current = 0;
        return;
      }
      holdRef.current += 1;
      if (holdRef.current > 160) setLineIdx((n) => n + 1);
    }, 16);
    return () => window.clearInterval(timer);
  }, [full, typedInterval, replay]);
  const activeHover = (0, import_react44.useMemo)(() => hover.filter((o) => o.on), [hover]);
  const scaleText = `${Math.round(clamp(scale, 0.4, 2.6) * 100)}%`;
  const fontText = `${Math.round(fontSize)} px`;
  const fpsText = `${Math.round(fps)} fps`;
  const speedText = `\xD7${bubbleSpeed.toFixed(1).replace(/\.0$/, "")}`;
  const purrText = `${Math.round(purr)} \u79D2`;
  const bodyHeight = clamp(168 * scale, 56, 264);
  const glowSize = bodyHeight * (1.15 + 0.5 * clamp(glow, 0, 1));
  const readout = `\u4F53\u578B ${scaleText} \xB7 \u5B57\u53F7 ${fontText} \xB7 \u53D1\u5149 ${Math.round(glow * 100)}% \xB7 ${fpsText}${fpsLock ? "\uFF08\u9501\u5B9A\uFF09" : "\uFF08\u653E\u5F00\u8DD1\uFF09"} \xB7 \u60AC\u505C ${activeHover.length} \u952E \xB7 \u6C14\u6CE1 \xD7${bubbleSpeed.toFixed(1).replace(/\.0$/, "")}`;
  const toggleHover = (id, on) => {
    if (on && hover.filter((o) => o.on).length >= 6) {
      setHoverHint("\u60AC\u505C\u6309\u94AE\u6700\u591A 6 \u4E2A\u2014\u2014\u5148\u79FB\u9664\u4E00\u4E2A\u518D\u6362\u3002");
      return;
    }
    setHoverHint("");
    setHover((list) => {
      const next = list.map((o) => o.id === id ? { ...o, on } : o);
      const ordered = next.filter((o) => o.on);
      return [...ordered, ...next.filter((o) => !o.on)];
    });
  };
  const move = (id, delta) => {
    setHover((list) => {
      const from = list.findIndex((o) => o.id === id);
      const to = from + delta;
      if (from < 0 || to < 0 || to >= list.length) return list;
      const next = [...list];
      const [item] = next.splice(from, 1);
      next.splice(to, 0, item);
      return next;
    });
  };
  const stage = /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Glass, { as: "section", tone: "card", radius: 12, style: { overflow: "hidden" }, children: /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)(
    "div",
    {
      style: {
        position: "relative",
        display: "grid",
        gridTemplateRows: "auto 1fr auto",
        minHeight: wide ? 420 : 400,
        height: wide ? void 0 : 400
      },
      children: [
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("div", { "aria-hidden": "true", style: { position: "absolute", inset: 0, background: "var(--w-bg)" } }),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("div", { "aria-hidden": "true", style: { position: "absolute", left: 0, right: 0, bottom: 0, height: 18, background: "var(--w-tint-track)" } }),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
          "div",
          {
            "aria-hidden": "true",
            style: {
              position: "absolute",
              left: 0,
              right: 0,
              bottom: 4,
              display: "flex",
              justifyContent: "center",
              gap: "var(--w-space-1)"
            },
            children: [0, 1, 2].map((i) => /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: { width: 8, height: 8, borderRadius: 4, background: "var(--w-hairline)" } }, i))
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)("div", { style: { position: "relative", zIndex: 3, display: "flex", flexDirection: "column", gap: "var(--w-space-1)", padding: "14px 14px 0" }, children: [
          statusBubble ? /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
            "div",
            {
              style: {
                alignSelf: "center",
                maxWidth: 300,
                padding: "6px 10px",
                borderRadius: 12,
                background: "var(--w-tint-track)"
              },
              children: /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: { color: "var(--w-ink-body)", fontSize, lineHeight: 1.5 }, children: typed || "\xA0" })
            }
          ) : null,
          inputBubble ? /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)(
            "div",
            {
              style: {
                alignSelf: "center",
                width: 260,
                padding: "5px 8px",
                borderRadius: 14,
                border: "1px solid var(--w-hairline)",
                background: "var(--w-tint-track)",
                display: "flex",
                alignItems: "center",
                gap: "var(--w-space-1)"
              },
              children: [
                /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: { flex: 1, color: "var(--w-ink-muted)", fontSize }, children: "\u548C TA \u8BF4\u70B9\u4EC0\u4E48\u2026" }),
                /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
                  "span",
                  {
                    style: {
                      padding: "2px 8px",
                      borderRadius: 9,
                      background: "var(--w-surface-tint)",
                      color: "var(--w-ink-label)",
                      fontSize: 11
                    },
                    children: "\u53D1\u9001"
                  }
                )
              ]
            }
          ) : null
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)("div", { style: { position: "relative", minHeight: 160 }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
            "div",
            {
              "aria-hidden": "true",
              style: {
                position: "absolute",
                left: "50%",
                bottom: 18,
                width: glowSize,
                height: glowSize,
                transform: "translateX(-50%)",
                opacity: clamp(glow, 0, 1) * 0.85,
                borderRadius: "50%",
                background: `radial-gradient(circle, color-mix(in srgb, var(--w-signal) 70%, transparent) 0%, color-mix(in srgb, var(--w-signal) 34%, transparent) 55%, transparent 100%)`
              }
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
            "div",
            {
              style: {
                position: "absolute",
                left: "50%",
                bottom: 18,
                width: bodyHeight,
                height: bodyHeight,
                transform: "translateX(-50%)",
                borderRadius: 90,
                background: "var(--w-tint-track)",
                display: "flex",
                alignItems: "center",
                justifyContent: "center"
              },
              children: /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: { fontSize: 15, color: "var(--w-ink-label)", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }, children: "(\u02F6\u1D54 \u1D55 \u1514\u02F6)" })
            }
          ),
          activeHover.length > 0 ? /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
            "div",
            {
              style: {
                position: "relative",
                display: "flex",
                justifyContent: "center",
                gap: "var(--w-space-1)",
                padding: "6px 2px"
              },
              children: activeHover.map((item) => /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
                "span",
                {
                  style: {
                    padding: "4px 10px",
                    borderRadius: 11,
                    background: "var(--w-tint-track)",
                    color: "var(--w-ink-body)",
                    fontSize: 13,
                    whiteSpace: "nowrap"
                  },
                  children: item.name
                },
                item.id
              ))
            }
          ) : null
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)("div", { style: { position: "relative", display: "grid", gridTemplateColumns: "minmax(0,1fr) auto", gap: "var(--w-space-2)", padding: "4px 14px 10px" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 0, justifyContent: "center" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: caption4, children: "\u5B9E\u65F6\u9884\u89C8" }),
            /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: { ...bodySm, lineHeight: 1.5 }, children: readout })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(GlassButton, { size: "sm", onClick: () => setReplay((n) => n + 1), children: "\u91CD\u65B0\u6F14\u793A" })
        ] })
      ]
    }
  ) });
  const settings = /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)", minWidth: 0, paddingBottom: 24 }, children: [
    hoverHint ? /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("p", { style: { ...caption4, color: "var(--w-ink-label)" }, children: hoverHint }) : null,
    /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Card, { children: /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "minmax(0,1fr) auto", gap: "var(--w-space-2)", alignItems: "center" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 3 }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: eyebrow, children: "// PET STUDIO" }),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: subhead3, children: "AI \u5236\u4F5C\u684C\u5BA0" }),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: rowDesc, children: "\u8FD8\u6CA1\u8FDB\u5B58\u6863\uFF0C\u4E0D\u77E5\u9053\u8BE5\u770B\u8C01\u7684\u7D20\u6750\u5305" }),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: caption4, children: "Q \u7248\u5316\u4E00\u6574\u5957\u72B6\u6001\u5E27\uFF1A\u6563\u6B65\u4F1A\u8FC8\u6B65\u3001\u88AB\u6478\u5934\u4F1A\u8DF3\u3001\u547C\u565C\u6BD4\u5FC3\uFF0C\u4E45\u4E86\u5C31\u5750\u4E0B\u3001\u7761\u7740" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(GlassButton, { size: "md", onClick: () => navigate("/petstudio"), children: "\u53BB\u5236\u4F5C\u2026" })
    ] }) }),
    /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("div", { style: { display: "flex", flexWrap: "wrap", gap: "var(--w-space-1)" }, children: SEGMENTS.map((name, i) => /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
      "button",
      {
        type: "button",
        onClick: () => setTab(i),
        style: {
          padding: "6px 12px",
          borderRadius: "var(--w-radius-button)",
          border: "1px solid",
          borderColor: i === tab ? "transparent" : "var(--w-hairline)",
          background: i === tab ? "var(--w-signal)" : "transparent",
          color: i === tab ? "var(--w-ink-on-signal)" : "var(--w-ink-body)",
          font: "inherit",
          fontSize: "var(--w-size-small)",
          cursor: "pointer"
        },
        children: name
      },
      name
    )) }),
    tab === 0 ? /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)(Card, { children: [
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(SliderRow, { title: "\u4F53\u578B", desc: "\u7AD9\u5728\u684C\u9762\u4E0A\u7684\u5927\u5C0F\uFF1B\u4E5F\u53EF\u4EE5\u5728\u89D2\u8272\u4E0A\u6EDA\u8F6E\u76F4\u63A5\u8C03", min: 0.4, max: 2.6, value: scale, onChange: setScale, readout: scaleText }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Sep, { bleed: true }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
        SliderRow,
        {
          title: "\u6C14\u6CE1\u5B57\u53F7",
          desc: "\u5934\u9876\u6C14\u6CE1\u4E0E\u8F93\u5165\u6761\u7684\u5B57\u6709\u591A\u5927\uFF1B\u6539\u5B8C\u6C14\u6CE1\u9AD8\u5EA6\u4F1A\u8DDF\u7740\u91CD\u65B0\u91CF",
          min: 10,
          max: 22,
          value: fontSize,
          onChange: setFontSize,
          readout: fontText,
          step: 1
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Sep, { bleed: true }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(SliderRow, { title: "\u5916\u53D1\u5149", desc: "\u6CBF\u89D2\u8272\u8F6E\u5ED3\u7684\u4E00\u5708\u67D4\u5149\uFF1B0 \u5C31\u662F\u6CA1\u6709", min: 0, max: 1, value: glow, onChange: setGlow, readout: "0\u20131" }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Sep, { bleed: true }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
        ChoiceRow,
        {
          title: "\u7ED1\u5B9A\u89D2\u8272",
          desc: "\u9ED8\u8BA4\u8DDF\u968F\u5F53\u524D\u5B58\u6863\u4E3B\u89D2\uFF1B\u6362\u6863\u65F6\u684C\u9762\u4E0A\u7684\u5979\u4F1A\u8DDF\u7740\u6362\uFF0C\u4E5F\u53EF\u4EE5\u9489\u4F4F\u67D0\u4E00\u4E2A\u89D2\u8272",
          value: binding,
          choices: options(["\u8DDF\u968F\u5B58\u6863\u4E3B\u89D2"]),
          onChange: setBinding,
          width: 180,
          first: true
        }
      )
    ] }) : null,
    tab === 1 ? /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)(import_jsx_runtime57.Fragment, { children: [
      /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)(Card, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
          SliderRow,
          {
            title: "\u52A8\u4F5C\u5E27\u7387",
            desc: "\u8D70\u52A8/\u547C\u565C\u8FD9\u4E9B\u81EA\u53D1\u52A8\u4F5C\u7684\u52A8\u753B\u5E27\u7387\uFF1B\u9884\u89C8\u91CC\u7684\u547C\u5438\u8282\u594F\u5C31\u662F\u8FD9\u4E2A\u6570",
            min: 10,
            max: 60,
            value: fps,
            onChange: setFps,
            readout: fpsText,
            step: 1
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Sep, { bleed: true }),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(SwitchRow, { title: "\u5E27\u7387\u9501\u5B9A", desc: "\u5F00=\u4E25\u683C\u6309\u5E27\u7387\u62BD\u5E27\uFF08\u7701\u7535\uFF09\uFF1B\u5173=\u6709\u4F59\u91CF\u5C31\u8DD1\u6EE1\u5230 60", checked: fpsLock, onChange: setFpsLock }),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Sep, { bleed: true }),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
          ChoiceRow,
          {
            title: "\u8D70\u52A8",
            desc: "\u5F00\u6563\u6B65\u540E\u5979\u4F1A\u6CBF\u5C4F\u5E55\u5E95\u6CBF\u6162\u6162\u8D70\u52A8\uFF0C\u78B0\u5230\u8FB9\u7F18\u81EA\u5DF1\u56DE\u5934",
            value: walking,
            choices: options(WALKING),
            onChange: setWalking,
            width: 150
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Sep, { bleed: true }),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(ChoiceRow, { title: "\u53CC\u51FB\u89D2\u8272", desc: "\u53CC\u51FB\u5979\u4E00\u4E0B\u8981\u53D1\u751F\u4EC0\u4E48", value: doubleClick, choices: options(DOUBLE_CLICK), onChange: setDoubleClick, width: 150 })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)(Card, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("p", { style: bodySm, children: "\u60AC\u505C\u6309\u94AE\uFF08\u6700\u591A 6 \u4E2A\uFF0C\u51FA\u73B0\u987A\u5E8F\u5C31\u662F\u8FD9\u91CC\u7684\u987A\u5E8F\uFF09" }),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("div", { style: { display: "flex", flexDirection: "column", gap: 0, margin: "6px 0 4px" }, children: hover.map((item) => /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)(
          "div",
          {
            style: {
              display: "grid",
              gridTemplateColumns: "minmax(0,1fr) auto auto auto",
              gap: "var(--w-space-2)",
              alignItems: "center",
              padding: "8px 0"
            },
            children: [
              /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: { ...bodySm, overflow: "hidden", whiteSpace: "nowrap", textOverflow: "ellipsis" }, children: item.name }),
              item.on ? /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
                "button",
                {
                  type: "button",
                  "aria-label": `${item.name}\u4E0A\u79FB`,
                  onClick: () => move(item.id, -1),
                  style: ARROW,
                  children: "\u2191"
                }
              ) : /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", {}),
              item.on ? /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
                "button",
                {
                  type: "button",
                  "aria-label": `${item.name}\u4E0B\u79FB`,
                  onClick: () => move(item.id, 1),
                  style: ARROW,
                  children: "\u2193"
                }
              ) : /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", {}),
              /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(GlassSwitch, { checked: item.on, onChange: (on) => toggleHover(item.id, on), label: item.name })
            ]
          },
          item.id
        )) })
      ] })
    ] }) : null,
    tab === 2 ? /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)(Card, { children: [
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(SwitchRow, { title: "\u72B6\u6001\u6C14\u6CE1", desc: "\u5934\u9876\u90A3\u4E00\u884C\u5C0F\u5B57\uFF1A\u5979\u73B0\u5728\u5728\u505A\u4EC0\u4E48\u3002\u5173\u6389\u5C31\u4E00\u4E2A\u5B57\u90FD\u4E0D\u5199", checked: statusBubble, onChange: setStatusBubble }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Sep, { bleed: true }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(SwitchRow, { title: "\u8F93\u5165\u6C14\u6CE1", desc: "\u5934\u9876\u90A3\u6761\u8F93\u5165\u6761\u3002\u5173\u4E86\u5C31\u53EA\u5269\u89D2\u8272\u672C\u4EBA", checked: inputBubble, onChange: setInputBubble }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Sep, { bleed: true }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
        SliderRow,
        {
          title: "\u6C14\u6CE1\u6D6E\u73B0\u901F\u5EA6",
          desc: "\u56DE\u590D\u9010\u5B57\u6D6E\u73B0\u7684\u8282\u594F\uFF1B\xD71 \u2248 \u6B63\u6587\u540C\u901F\uFF0C\u8D8A\u5927\u8D8A\u5FEB",
          min: 0.2,
          max: 6,
          value: bubbleSpeed,
          onChange: setBubbleSpeed,
          readout: speedText
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Sep, { bleed: true }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "minmax(0,1fr) auto auto", gap: "var(--w-space-2)", alignItems: "center", padding: "10px 0" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 2 }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: bodySm, children: "\u6253\u5B57\u97F3" }),
          /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: rowDesc, children: "\u6C14\u6CE1\u9010\u5B57\u6D6E\u73B0\u65F6\u7684\u90A3\u4E32\u5C0F\u952E\u76D8\u58F0" })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(GlassButton, { size: "sm", children: "\u8BD5\u542C" }),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(GlassSwitch, { checked: typingSound, onChange: setTypingSound, label: "\u6253\u5B57\u97F3" })
      ] })
    ] }) : null,
    tab === 3 ? /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)(Card, { children: [
      /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "minmax(0,1fr) 150px 72px 44px", gap: "var(--w-space-2)", alignItems: "center", padding: "10px 0" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 2 }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: bodySm, children: "\u5355\u51FB\u97F3\u6548" }),
          /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: caption4, children: "\u70B9\u5979\u4E00\u4E0B\u3001\u6309\u60AC\u505C\u6309\u94AE\u65F6\u7684\u90A3\u4E00\u58F0" })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(GlassSlider, { label: "\u5355\u51FB\u97F3\u6548", width: 150, value: tapLevel, onChange: setTapLevel, format: () => `${Math.round(tapLevel * 100)}%` }),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)("span", { style: { ...caption4, textAlign: "right" }, children: [
          Math.round(tapLevel * 100),
          "%"
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(GlassButton, { size: "sm", children: "\u8BD5\u542C" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Sep, { bleed: true }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "minmax(0,1fr) 150px 72px 44px", gap: "var(--w-space-2)", alignItems: "center", padding: "10px 0" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 2 }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: bodySm, children: "\u6253\u5B57\u97F3" }),
          /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: caption4, children: "\u6C14\u6CE1\u9010\u5B57\u6D6E\u73B0\u65F6\u90A3\u4E32\u5C0F\u952E\u76D8\u58F0\uFF1B\u5F00\u5173\u5728\u300C\u6C14\u6CE1\u300D\u6BB5\u91CC" })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(GlassSlider, { label: "\u6253\u5B57\u97F3\u97F3\u91CF", width: 150, value: typingLevel, onChange: setTypingLevel, format: () => `${Math.round(typingLevel * 100)}%` }),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)("span", { style: { ...caption4, textAlign: "right" }, children: [
          Math.round(typingLevel * 100),
          "%"
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(GlassButton, { size: "sm", children: "\u8BD5\u542C" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Sep, { bleed: true }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
        SliderRow,
        {
          title: "\u6492\u5A07\u65F6\u957F",
          desc: "\u6309\u300C\u547C\u565C/\u6478\u6478\u5934\u300D\u65F6\u5979\u6296\u591A\u4E45\uFF0C\u72B6\u6001\u884C\u4E5F\u5199\u591A\u4E45",
          min: 2,
          max: 20,
          value: purr,
          onChange: setPurr,
          readout: purrText,
          step: 1
        }
      )
    ] }) : null,
    tab === 4 ? /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)(Card, { children: [
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
        SwitchRow,
        {
          title: "\u56DE\u590D\u8BFB\u51FA\u58F0",
          desc: "\u5979\u7B54\u5B8C\u4E00\u53E5\uFF0C\u7528 TTS \u5FF5\u51FA\u6765\uFF1B\u5F15\u64CE\u4E0E\u5F00\u5173\u5728\u8BBE\u7F6E \u203A \u58F0\u97F3\u91CC",
          checked: speakReplies,
          onChange: setSpeakReplies
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Sep, { bleed: true }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
        ChoiceRow,
        {
          title: "\u97F3\u8272",
          desc: "\u8DDF\u968F\u5168\u5C40=\u7528\u8BBE\u7F6E\u91CC\u7684\u97F3\u8272\uFF1BAPI \u6A21\u5F0F\u8BA4 alloy / nova \u8FD9\u4E9B\uFF0C\u672C\u5730\u5F15\u64CE\u8BA4\u7CFB\u7EDF\u58F0\u97F3\u540D",
          value: voice,
          choices: options(VOICES),
          onChange: setVoice,
          width: 180
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Sep, { bleed: true }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(SliderRow, { title: "\u97F3\u9AD8", desc: "\xD71 \u662F\u539F\u58F0\uFF1B\u8C03\u5C0F\u66F4\u4F4E\u6C89\uFF0C\u8C03\u5927\u66F4\u8F6F", min: 0.5, max: 2, value: pitch, onChange: setPitch, readout: `\xD7${pitch.toFixed(2)}` }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Sep, { bleed: true }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(SliderRow, { title: "\u8BED\u901F", desc: "\u4E0E\u5168\u5C40\u8BED\u901F\u76F8\u4E58\uFF0C\u4E0D\u662F\u66FF\u6362", min: 0.5, max: 2, value: rate, onChange: setRate, readout: `\xD7${rate.toFixed(2)}` }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Sep, { bleed: true }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
        ChoiceRow,
        {
          title: "\u56DE\u5E94\u6A21\u5F0F",
          desc: "\u6709\u8BDD\u5C31\u8BF4=\u6309\u300C\u4E3B\u52A8\u601D\u8003\u300D\u81EA\u5DF1\u5F00\u53E3\uFF1B\u88AB\u70B9\u5230\u624D\u7B54=\u53EA\u5728\u4F60\u70B9\u5979\u65F6\u56DE\u5E94\uFF1B\u5C11\u63D2\u8BDD=\u4E3B\u52A8\u5F00\u53E3\u51CF\u5230\u5076\u53D1",
          value: respond,
          choices: options(RESPOND),
          onChange: setRespond,
          width: 150
        }
      )
    ] }) : null,
    tab === 5 ? /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)(Card, { children: [
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
        SwitchRow,
        {
          title: "\u8BB0\u4F4F\u4F4D\u7F6E",
          desc: "\u62D6\u5230\u54EA\u513F\u4E0B\u6B21\u8FD8\u5728\u54EA\u513F\uFF1B\u5173\u4E86\u5219\u6BCF\u6B21\u56DE\u5230\u5DE5\u4F5C\u533A\u53F3\u4E0B\u89D2",
          checked: remember,
          onChange: setRemember
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Sep, { bleed: true }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "minmax(0,1fr) auto auto", gap: "var(--w-space-2)", alignItems: "center", padding: "10px 0" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 2 }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: bodySm, children: "\u5F53\u524D\u951A\u70B9" }),
          /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("span", { style: rowDesc, children: "\u8FD8\u6CA1\u8BB0\u8FC7\u4F4D\u7F6E\u2014\u2014\u62D6\u5230\u559C\u6B22\u7684\u5730\u65B9\u518D\u70B9\u300C\u8BB0\u4F4F\u5F53\u524D\u4F4D\u7F6E\u300D\u3002" })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(GlassButton, { size: "sm", children: "\u8BB0\u4F4F\u5F53\u524D\u4F4D\u7F6E" }),
        /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(GlassButton, { size: "sm", children: "\u6E05\u9664" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Sep, { bleed: true }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
        SwitchRow,
        {
          title: "\u5168\u5C4F\u65F6\u6536\u8D77",
          desc: "\u524D\u53F0\u8FDB\u5165\u5168\u5C4F\uFF08\u653E\u6620/\u6E38\u620F\uFF09\u65F6\u628A\u5979\u4E00\u8D77\u6536\u8D70\uFF0C\u9000\u51FA\u5168\u5C4F\u518D\u56DE\u6765",
          checked: hideFullscreen,
          onChange: setHideFullscreen
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Sep, { bleed: true }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
        SwitchRow,
        {
          title: "\u5C0F\u7A97\u8DE8\u5C4F\u8DDF\u968F",
          desc: "\u591A\u663E\u793A\u5668\uFF1A\u5F00=\u5934\u9876\u5C0F\u7A97\u53EF\u4EE5\u8DDF\u89D2\u8272\u8DE8\u5230\u526F\u5C4F\uFF1B\u5173=\u53EA\u5728\u672C\u5C4F\u5DE5\u4F5C\u533A\u6536\u8FB9",
          checked: crossScreen,
          onChange: setCrossScreen
        }
      )
    ] }) : null,
    tab === 6 ? /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)(Card, { children: [
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
        ChoiceRow,
        {
          title: "\u684C\u5BA0\u8BED\u8A00",
          desc: "\u5979\u5934\u9876\u90A3\u884C\u72B6\u6001\u5B57\u7528\u54EA\u4E00\u79CD\uFF1B\u7A7A=\u8DDF\u968F\u5168\u5C40\u8BED\u8A00",
          value: language,
          choices: options(LANGUAGES),
          onChange: setLanguage,
          width: 150,
          first: true
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Sep, { bleed: true }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
        ChoiceRow,
        {
          title: "\u95F2\u7F6E\u81EA\u52A8\u684C\u5BA0",
          desc: "\u95F2\u7F6E\u591A\u5C11\u5206\u949F\u540E\u81EA\u52A8\u6536\u7EB3\u6210\u684C\u5BA0\uFF1B\u5173\u95ED=\u4E0D\u81EA\u52A8\u8FDB",
          value: idle,
          choices: IDLE.map((n) => ({ value: String(n), label: String(n) })),
          onChange: setIdle,
          width: 120
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Sep, { bleed: true }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(ChoiceRow, { title: "\u4E3B\u52A8\u601D\u8003", desc: "\u6CA1\u4EBA\u6253\u6270\u65F6\u5979\u81EA\u5DF1\u5600\u5495\u4E00\u53E5", value: think, choices: options(THINK), onChange: setThink, width: 150 }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Sep, { bleed: true }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
        ChoiceRow,
        {
          title: "\u601D\u8003\u95F4\u9694",
          value: thinkInterval,
          choices: THINK_INTERVAL.map((n) => ({ value: String(n), label: String(n) })),
          onChange: setThinkInterval,
          width: 120
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(Sep, { bleed: true }),
      /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
        SwitchRow,
        {
          title: "\u8BA9\u5979\u81EA\u5DF1\u8C03\u684C\u5BA0\u8BBE\u7F6E",
          desc: "\u5F00=\u5979\u53EF\u4EE5\u5728\u56DE\u590D\u91CC\u5E26\u4E00\u53E5 pet \u6307\u4EE4\uFF0C\u76F4\u63A5\u6539\u4F53\u578B/\u53D1\u5149/\u5B57\u53F7\uFF1B\u9ED8\u8BA4\u5173\uFF0C\u514D\u5F97\u5979\u7684\u6539\u52A8\u6084\u6084\u76D6\u6389\u4F60\u7684\u504F\u597D",
          checked: selfConfig,
          onChange: setSelfConfig
        }
      )
    ] }) : null
  ] });
  return /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(
      PageHeader,
      {
        eyebrow: "// DESKTOP PET",
        title: "\u684C\u5BA0\u8BBE\u7F6E",
        subtitle: "\u6539\u4E86\u7ACB\u523B\u751F\u6548\u2014\u2014\u5DE6\u8FB9\u5C31\u662F\u5979",
        onBack: back,
        right: /* @__PURE__ */ (0, import_jsx_runtime57.jsx)(GlassButton, { size: "sm", onClick: () => navigate("/usagestats"), children: "\u4F7F\u7528\u7EDF\u8BA1\u2026" })
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime57.jsx)("main", { className: "screen", children: /* @__PURE__ */ (0, import_jsx_runtime57.jsxs)(
      "div",
      {
        style: {
          display: "grid",
          gridTemplateColumns: wide ? "440px minmax(0,1fr)" : "minmax(0,1fr)",
          gap: "var(--w-space-3)",
          alignItems: "start"
        },
        children: [
          stage,
          settings
        ]
      }
    ) })
  ] });
}
var ARROW = {
  padding: "2px 8px",
  height: 28,
  borderRadius: "var(--w-radius-button)",
  border: "1px solid var(--w-hairline)",
  background: "transparent",
  color: "var(--w-ink-body)",
  font: "inherit",
  fontSize: "var(--w-size-small)",
  cursor: "pointer"
};

// src/screens/settings-misc/petstudio.tsx
var import_jsx_runtime58 = __toESM(require_jsx_runtime(), 1);
var STATES = [
  { id: "normal", name: "\u666E\u901A", frames: 1 },
  { id: "happy", name: "\u5F00\u5FC3", frames: 1 },
  { id: "sit", name: "\u5750\u4E0B", frames: 1 },
  { id: "stand", name: "\u7AD9\u8D77", frames: 1 },
  { id: "walk", name: "\u8D70\u52A8", frames: 6 },
  { id: "jump", name: "\u8DF3\u8DC3", frames: 4 },
  { id: "love", name: "\u559C\u7231", frames: 1 },
  { id: "angry", name: "\u751F\u6C14", frames: 1 },
  { id: "cry", name: "\u6D41\u6CEA", frames: 1 },
  { id: "surprised", name: "\u60CA\u8BB6", frames: 1 },
  { id: "sleep", name: "\u7761\u7740", frames: 1 }
];
var clamp2 = (v, min, max) => Math.min(max, Math.max(min, v));
function cadenceText() {
  const bodyHeight = clamp2(320, 96, 720) * 1;
  const cycleSeconds = Math.max(1, bodyHeight) / 120;
  const fps = clamp2(6 / cycleSeconds, 6, 10);
  return `\u8D70\u52A8\u5FAA\u73AF\uFF1A6 \u5E27 \xB7 ${Number(fps.toFixed(1))} fps \xB7 ${Math.round(1e3 / fps)} ms/\u5E27\uFF08\u6309\u6B65\u9891\u7B97\uFF0C\u975E\u5199\u6B7B\uFF09`;
}
var IMAGE_HINT = "\u753B\u9762\u751F\u6210\u8FD8\u6CA1\u5F00\u542F\uFF08\u8BBE\u7F6E \u2192 AI \u2192 \u753B\u9762\u751F\u6210 \u91CC\u586B\u597D\u63A5\u53E3\u4E0E\u5BC6\u94A5\uFF09\uFF0C\u73B0\u5728\u6CA1\u6CD5\u51FA\u56FE\u3002";
var subhead4 = {
  margin: 0,
  fontSize: "var(--w-size-subhead)",
  fontWeight: 600,
  color: "var(--w-ink-display)"
};
var rowTitle3 = { margin: 0, fontSize: "var(--w-size-body)", color: "var(--w-ink-body)" };
var caption5 = { margin: 0, fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)", lineHeight: 1.6 };
function Petstudio({ back }) {
  return /* @__PURE__ */ (0, import_jsx_runtime58.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime58.jsx)(
      PageHeader,
      {
        title: "AI \u5236\u4F5C\u684C\u5BA0",
        onBack: back,
        right: /* @__PURE__ */ (0, import_jsx_runtime58.jsx)(
          "span",
          {
            style: {
              maxWidth: 280,
              padding: "5px 10px",
              borderRadius: 99,
              background: "var(--w-surface-tint)",
              color: "var(--w-ink-label)",
              fontSize: "var(--w-size-small)",
              whiteSpace: "nowrap",
              overflow: "hidden",
              textOverflow: "ellipsis"
            },
            children: cadenceText()
          }
        )
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime58.jsx)("main", { className: "screen", children: /* @__PURE__ */ (0, import_jsx_runtime58.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)", width: "100%", maxWidth: 1e3, margin: "0 auto" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime58.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime58.jsx)("p", { style: subhead4, children: "\u2460 \u6311\u4E00\u4F4D\u89D2\u8272" }),
        /* @__PURE__ */ (0, import_jsx_runtime58.jsx)(Card, { children: /* @__PURE__ */ (0, import_jsx_runtime58.jsx)("p", { style: { ...caption5, padding: "10px 0" }, children: "\u89D2\u8272\u5E93\u91CC\u8FD8\u6CA1\u6709\u89D2\u8272\uFF0C\u5148\u53BB\u4EBA\u7269\u5DE5\u4F5C\u5BA4\u5EFA\u4E00\u4F4D\u3002" }) })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime58.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime58.jsx)("p", { style: subhead4, children: "\u2461 \u4E00\u952E\u751F\u6210\u6574\u5957\u72B6\u6001\u52A8\u753B" }),
        /* @__PURE__ */ (0, import_jsx_runtime58.jsxs)(Card, { children: [
          /* @__PURE__ */ (0, import_jsx_runtime58.jsx)("p", { style: caption5, children: "\u4EE3\u7801\u5148\u6309\u8D70\u52A8\u901F\u5EA6\u7B97\u597D\u8D70\u52A8\u5FAA\u73AF\u7684\u5E27\u6570\u4E0E\u6B65\u9891\uFF0C\u518D\u9010\u72B6\u6001\u4E32\u884C\u51FA\u56FE\uFF1B\u6BCF\u51FA\u4E00\u5E27\u5C31\u70B9\u4EAE\u4E0B\u9762\u5BF9\u5E94\u7684\u4E00\u884C\u3002\u8FDB\u5EA6\u662F\u771F\u7684\uFF0C\u4E0D\u4F1A\u7528\u5047\u52A8\u753B\u7CCA\u5F04\u4F60\u3002" }),
          /* @__PURE__ */ (0, import_jsx_runtime58.jsx)("p", { className: "s-warn", style: { marginTop: 0 }, children: IMAGE_HINT }),
          /* @__PURE__ */ (0, import_jsx_runtime58.jsxs)("div", { style: { display: "flex", gap: "var(--w-space-2)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime58.jsx)(SignalButton, { children: "\u4E00\u952E\u751F\u6210\u684C\u5BA0\u52A8\u753B" }),
            /* @__PURE__ */ (0, import_jsx_runtime58.jsx)(GlassButton, { size: "md", children: "\u53D6\u6D88\u751F\u6210" })
          ] })
        ] })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime58.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime58.jsx)("p", { style: subhead4, children: "\u2462 \u72B6\u6001\u52A8\u753B\u4E0E\u9884\u89C8" }),
        /* @__PURE__ */ (0, import_jsx_runtime58.jsx)(Card, { children: /* @__PURE__ */ (0, import_jsx_runtime58.jsx)("div", { style: { display: "flex", flexDirection: "column" }, children: STATES.map((state, i) => /* @__PURE__ */ (0, import_jsx_runtime58.jsxs)("div", { children: [
          /* @__PURE__ */ (0, import_jsx_runtime58.jsxs)(
            "div",
            {
              style: {
                display: "grid",
                gridTemplateColumns: "64px minmax(0,1fr) auto",
                gap: "var(--w-space-2)",
                alignItems: "center",
                padding: "10px 0"
              },
              children: [
                /* @__PURE__ */ (0, import_jsx_runtime58.jsx)(
                  "div",
                  {
                    "aria-hidden": "true",
                    style: { height: 64, borderRadius: "var(--w-radius-well)", background: "var(--w-surface-tint)" }
                  }
                ),
                /* @__PURE__ */ (0, import_jsx_runtime58.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 2 }, children: [
                  /* @__PURE__ */ (0, import_jsx_runtime58.jsx)("span", { style: { ...rowTitle3, whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }, children: state.name }),
                  /* @__PURE__ */ (0, import_jsx_runtime58.jsx)("span", { style: { ...caption5, whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }, children: "\u5F85\u751F\u6210" })
                ] }),
                /* @__PURE__ */ (0, import_jsx_runtime58.jsxs)(
                  "span",
                  {
                    style: {
                      padding: "5px 10px",
                      borderRadius: 99,
                      background: "var(--w-surface-tint)",
                      color: "var(--w-ink-label)",
                      fontSize: "var(--w-size-caption)",
                      whiteSpace: "nowrap"
                    },
                    children: [
                      "0/",
                      state.frames,
                      " \u5E27"
                    ]
                  }
                )
              ]
            }
          ),
          i < STATES.length - 1 ? /* @__PURE__ */ (0, import_jsx_runtime58.jsx)(Sep, {}) : null
        ] }, state.id)) }) }),
        /* @__PURE__ */ (0, import_jsx_runtime58.jsx)("p", { style: caption5, children: "\u8D70\u52A8\u3001\u8DF3\u8DC3\u662F\u591A\u5E27\u5FAA\u73AF\uFF0C\u5176\u4F59\u72B6\u6001\u591A\u4E3A\u5355\u5E27\uFF1B\u751F\u6210\u5B8C\u6210\u540E\u53EF\u5728\u684C\u5BA0\u8BBE\u7F6E\u91CC\u628A\u8FD9\u5957\u7D20\u6750\u5305\u63A5\u4E0A\u64AD\u653E\u3002" })
      ] })
    ] }) })
  ] });
}

// src/screens/settings-misc/usagestats.tsx
var import_react45 = __toESM(require_react(), 1);
var import_jsx_runtime59 = __toESM(require_jsx_runtime(), 1);
var section = {
  margin: 0,
  fontSize: "var(--w-size-subhead)",
  fontWeight: 600,
  color: "var(--w-ink-display)"
};
var caption6 = { fontSize: "var(--w-size-caption)", color: "var(--w-ink-muted)" };
var muted2 = {
  margin: 0,
  fontSize: "var(--w-size-small)",
  color: "var(--w-ink-muted)",
  lineHeight: 1.6
};
var bodySm2 = {
  margin: 0,
  fontSize: "var(--w-size-small)",
  color: "var(--w-ink-body)",
  lineHeight: 1.5
};
var display = {
  fontSize: "var(--w-size-display)",
  fontWeight: 600,
  color: "var(--w-ink-display)",
  fontVariantNumeric: "tabular-nums",
  whiteSpace: "nowrap",
  overflow: "hidden",
  textOverflow: "ellipsis"
};
var HEAT_DAYS = ["\u5468\u4E00", "\u5468\u4E8C", "\u5468\u4E09", "\u5468\u56DB", "\u5468\u4E94", "\u5468\u516D", "\u5468\u65E5"];
var HEAT_EMPTY = "color-mix(in srgb, var(--w-signal) 10%, transparent)";
var FMT_OVERVIEW_UNIT = "\u672A\u5F55\u5355\u4EF7\uFF0C\u53EA\u7EDF\u8BA1 token";
var FMT_EMPTY_HINT = "\u8FD8\u6CA1\u6709\u7528\u91CF\u8BB0\u5F55\u2014\u2014\u7AEF\u70B9\u8FD4\u56DE usage \u540E\u4F1A\u81EA\u52A8\u8BB0\u8D26\uFF08\u90E8\u5206\u670D\u52A1\u4E0D\u8FD4\u56DE usage\uFF0C\u5219\u65E0\u6CD5\u7EDF\u8BA1\uFF09\u3002";
var FMT_COST_NOTE = "\u672A\u5F55\u5355\u4EF7\uFF0C\u53EA\u7EDF\u8BA1 token\u2014\u2014\u5728\u4E0B\u65B9\u300C\u6A21\u578B\u4EF7\u683C\u8BBE\u5B9A\u300D\u5F55\u5165\u540E\u8FD9\u91CC\u81EA\u52A8\u53D8\u6210\u6210\u672C\u6784\u6210\u3002";
var FMT_BREAKDOWN = "\u7D2F\u8BA1\u8F93\u5165 0\uFF08\u5176\u4E2D\u7F13\u5B58\u547D\u4E2D 0\uFF09 \xB7 \u7D2F\u8BA1\u8F93\u51FA 0\uFF08\u5916\u52A0\u601D\u8003/\u7F13\u5B58 0\uFF09";
function lastDays(n) {
  const today = /* @__PURE__ */ new Date();
  today.setHours(0, 0, 0, 0);
  return Array.from({ length: n }, (_, i) => new Date(today.getTime() - (n - 1 - i) * 864e5));
}
var mmdd = (d) => `${`${d.getMonth() + 1}`.padStart(2, "0")}-${`${d.getDate()}`.padStart(2, "0")}`;
function SmallBtn({ label, active, onClick }) {
  return /* @__PURE__ */ (0, import_jsx_runtime59.jsx)(
    "button",
    {
      type: "button",
      onClick,
      style: {
        padding: "6px 12px",
        borderRadius: "var(--w-radius-button)",
        border: "1px solid",
        borderColor: active ? "transparent" : "var(--w-hairline)",
        background: active ? "color-mix(in srgb, var(--w-signal) 30%, transparent)" : "transparent",
        color: active ? "var(--w-ink-display)" : "var(--w-ink-body)",
        font: "inherit",
        fontSize: "var(--w-size-small)",
        cursor: "pointer",
        whiteSpace: "nowrap"
      },
      children: label
    }
  );
}
function Usagestats({ back }) {
  const wide = useIsWide(820);
  const [trendDays, setTrendDays] = (0, import_react45.useState)(14);
  const [detailGroup, setDetailGroup] = (0, import_react45.useState)("model");
  const [pricesOpen, setPricesOpen] = (0, import_react45.useState)(false);
  const [priceNote, setPriceNote] = (0, import_react45.useState)("");
  const [newPrice, setNewPrice] = (0, import_react45.useState)({ model: "", input: "", output: "", cachedInput: "", cachedOutput: "" });
  const days = lastDays(trendDays);
  const slot = trendDays >= 24 ? 11 : trendDays >= 12 ? 20 : 40;
  const bar = trendDays >= 24 ? 8 : trendDays >= 12 ? 16 : 34;
  const left = /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-3)", minWidth: 0 }, children: [
    /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("p", { style: section, children: FMT_OVERVIEW_UNIT }),
      /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 1px 1fr 1px 1fr", gap: "var(--w-space-3)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime59.jsx)(HeadBlock, { label: "\u4ECA\u65E5", head: "0", meta: "0 \u6B21\u8C03\u7528 \xB7 0 tok" }),
        /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("span", { style: { background: "var(--w-hairline)" } }),
        /* @__PURE__ */ (0, import_jsx_runtime59.jsx)(HeadBlock, { label: "\u8FD1 7 \u5929", head: "0", meta: "0 \u6B21\u8C03\u7528 \xB7 0 tok" }),
        /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("span", { style: { background: "var(--w-hairline)" } }),
        /* @__PURE__ */ (0, import_jsx_runtime59.jsx)(HeadBlock, { label: "\u7D2F\u8BA1", head: "0", meta: "0 \u6B21\u8C03\u7528 \xB7 0 tok", sub: "\u5F55\u5165\u5355\u4EF7\u540E\u8FD9\u91CC\u663E\u793A\u5747\u4EF7" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("p", { style: { ...muted2, margin: 0 }, children: FMT_BREAKDOWN }),
      /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("p", { style: { ...caption6, margin: 0, lineHeight: 1.6 }, children: FMT_EMPTY_HINT })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("p", { style: section, children: "\u6210\u672C\u6784\u6210" }),
      /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("p", { style: muted2, children: FMT_COST_NOTE })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("p", { style: section, children: "\u65F6\u95F4\u5E8F\u5217\uFF08\u6309\u5929\uFF09" }),
      /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)(Card, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("div", { style: { display: "flex", gap: "var(--w-space-2)" }, children: [7, 14, 30].map((n) => /* @__PURE__ */ (0, import_jsx_runtime59.jsx)(SmallBtn, { label: `${n} \u5929`, active: trendDays === n, onClick: () => setTrendDays(n) }, n)) }),
        /* @__PURE__ */ (0, import_jsx_runtime59.jsx)(
          "div",
          {
            style: {
              height: 116,
              display: "flex",
              justifyContent: "center",
              alignItems: "flex-end",
              overflow: "hidden"
            },
            "aria-label": `\u6700\u8FD1 ${trendDays} \u5929\u7528\u91CF`,
            children: days.map((day) => /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("div", { style: { width: slot, display: "flex", flexDirection: "column", justifyContent: "flex-end" }, children: /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("div", { style: { display: "flex", justifyContent: "center" }, "aria-hidden": "true", children: /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("div", { style: { width: bar }, title: `${mmdd(day)} \xB7 0 \u6B21 \xB7 \u8F93\u5165 0\uFF08\u7F13\u5B58 0\uFF09 \xB7 \u8F93\u51FA 0\uFF08\u601D\u8003/\u7F13\u5B58 0\uFF09` }) }) }, day.toISOString()))
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)("div", { style: { display: "flex", justifyContent: "space-between", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("span", { style: { ...caption6, overflow: "hidden", whiteSpace: "nowrap", textOverflow: "ellipsis" }, children: mmdd(days[0]) }),
          /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("span", { style: { ...caption6, overflow: "hidden", whiteSpace: "nowrap", textOverflow: "ellipsis" }, children: mmdd(days[days.length - 1]) })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)("p", { style: bodySm2, children: [
          "\u6700\u8FD1 ",
          trendDays,
          " \u5929\uFF1A0 \u6B21 \xB7 0 tok \xB7 \u7F13\u5B58\u547D\u4E2D 0 tok"
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("p", { style: { ...caption6, margin: 0, lineHeight: 1.6 }, children: "\u6DF1\u8272\u6BB5 = \u5F53\u65E5\u8D70\u7F13\u5B58\u4EF7\u7684\u8F93\u5165 token\uFF1B\u70B9\u67F1\u5B50\u770B\u5F53\u5929\u660E\u7EC6\u3002" })
      ] })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)("div", { style: { display: "flex", alignItems: "flex-start", justifyContent: "space-between", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("p", { style: { ...section, marginLeft: 4 }, children: "\u5206\u7EC4\u660E\u7EC6" }),
        /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)("div", { style: { display: "flex", gap: "var(--w-space-1)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime59.jsx)(SmallBtn, { label: "\u6309\u6A21\u578B", active: detailGroup === "model", onClick: () => setDetailGroup("model") }),
          /* @__PURE__ */ (0, import_jsx_runtime59.jsx)(SmallBtn, { label: "\u6309\u5929", active: detailGroup === "day", onClick: () => setDetailGroup("day") }),
          /* @__PURE__ */ (0, import_jsx_runtime59.jsx)(SmallBtn, { label: "\u6309\u89D2\u8272", active: detailGroup === "character", onClick: () => setDetailGroup("character") })
        ] })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("div", { style: { overflowX: "auto", scrollbarWidth: "none" }, children: /* @__PURE__ */ (0, import_jsx_runtime59.jsx)(Card, { children: /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)("div", { style: { minWidth: 560, display: "flex", flexDirection: "column" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("div", { style: TABLE_ROW, children: TABLE_HEADS.map((head, i) => /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("span", { style: { ...caption6, textAlign: i === 0 ? "left" : "right" }, children: head }, head)) }),
        /* @__PURE__ */ (0, import_jsx_runtime59.jsx)(Sep, {}),
        /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("p", { style: { ...caption6, margin: 0, padding: "10px 12px" }, children: "\u8FD8\u6CA1\u6709\u53EF\u5206\u7EC4\u7684\u8BB0\u5F55\u3002" })
      ] }) }) })
    ] })
  ] });
  const right = /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-3)", minWidth: 0 }, children: [
    /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("p", { style: section, children: "Token \u6D3B\u8DC3\u5EA6\uFF08\u661F\u671F \xD7 \u5C0F\u65F6\uFF09" }),
      /* @__PURE__ */ (0, import_jsx_runtime59.jsx)(Card, { children: /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 3 }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "32px 1fr", gap: "var(--w-space-1)", alignItems: "center" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("span", { style: caption6, children: "\u65F6" }),
          /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("div", { style: { display: "flex", gap: 2 }, children: Array.from({ length: 24 }, (_, h) => /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("span", { style: { width: 11, fontSize: 8, textAlign: "center", color: "var(--w-ink-muted)" }, children: h % 6 === 0 ? h : "" }, h)) })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("div", { style: { display: "flex", flexDirection: "column", gap: 2 }, children: HEAT_DAYS.map((day) => /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "32px 1fr", gap: "var(--w-space-1)", alignItems: "center" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("span", { style: { ...caption6, overflow: "hidden", whiteSpace: "nowrap", textOverflow: "ellipsis" }, children: day }),
          /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("div", { style: { display: "flex", gap: 2 }, children: Array.from({ length: 24 }, (_, h) => /* @__PURE__ */ (0, import_jsx_runtime59.jsx)(
            "span",
            {
              title: `${day} ${h}:00\u2013${(h + 1) % 24}:00 \xB7 0 \u6B21 \xB7 0 tok`,
              style: { width: 11, height: 15, borderRadius: 2, background: HEAT_EMPTY }
            },
            h
          )) })
        ] }, day)) }),
        /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("p", { style: { ...caption6, margin: "6px 0 0", lineHeight: 1.6 }, children: "\u70B9\u683C\u5B50\u770B\u8BE5\u5C0F\u65F6\u660E\u7EC6\uFF1B\u989C\u8272\u8D8A\u6DF1\u8D8A\u6D3B\u8DC3\u3002" })
      ] }) })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)(Card, { children: [
      /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("p", { style: section, children: "\u6A21\u578B\u4EF7\u683C\u8BBE\u5B9A" }),
        /* @__PURE__ */ (0, import_jsx_runtime59.jsx)(TextBtn, { onClick: () => setPricesOpen((open) => !open), children: pricesOpen ? "\u6536\u8D77" : "\u5C55\u5F00" })
      ] }),
      pricesOpen ? /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-2)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("p", { style: muted2, children: "\u7B97\u94B1\u53E3\u5F84\uFF1A(\u8F93\u5165 \u2212 \u7F13\u5B58\u8F93\u5165)\xD7\u8F93\u5165\u4EF7 \uFF0B \u7F13\u5B58\u8F93\u5165\xD7\u7F13\u5B58\u8F93\u5165\u4EF7 \uFF0B \u8F93\u51FA\xD7\u8F93\u51FA\u4EF7 \uFF0B \u7F13\u5B58\u8F93\u51FA\xD7\u7F13\u5B58\u8F93\u51FA\u4EF7\u3002\u7F13\u5B58\u8F93\u5165\u672C\u5C31\u7B97\u5728\u8F93\u5165\u91CC\uFF0C\u6240\u4EE5\u662F\u51CF\u6CD5\u6539\u4EF7\u3001\u4E0D\u662F\u52A0\u4E00\u7B14\u3002" }),
        /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)(Card, { children: [
          /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("p", { style: { ...bodySm2, fontWeight: 600 }, children: "\uFF0B \u7ED9\u6CA1\u51FA\u73B0\u8FC7\u7684\u6A21\u578B\u5F55\u4EF7" }),
          /* @__PURE__ */ (0, import_jsx_runtime59.jsx)(
            Field,
            {
              value: newPrice.model,
              onChange: (model) => setNewPrice({ ...newPrice, model }),
              placeholder: "\u6A21\u578B\u540D\uFF08\u4E0E\u8BF7\u6C42\u91CC\u4E00\u81F4\uFF09",
              label: "\u6A21\u578B\u540D"
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--w-space-1)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime59.jsx)(
              Field,
              {
                value: newPrice.input,
                onChange: (input) => setNewPrice({ ...newPrice, input }),
                placeholder: "\u8F93\u5165\u4EF7/\u767E\u4E07",
                label: "\u8F93\u5165\u4EF7/\u767E\u4E07",
                type: "number"
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime59.jsx)(
              Field,
              {
                value: newPrice.output,
                onChange: (output) => setNewPrice({ ...newPrice, output }),
                placeholder: "\u8F93\u51FA\u4EF7/\u767E\u4E07",
                label: "\u8F93\u51FA\u4EF7/\u767E\u4E07",
                type: "number"
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime59.jsx)(
              Field,
              {
                value: newPrice.cachedInput,
                onChange: (cachedInput) => setNewPrice({ ...newPrice, cachedInput }),
                placeholder: "\u7F13\u5B58\u8F93\u5165\u4EF7/\u767E\u4E07\uFF08\u53EF\u7559\u7A7A\uFF09",
                label: "\u7F13\u5B58\u8F93\u5165\u4EF7/\u767E\u4E07\uFF08\u53EF\u7559\u7A7A\uFF09",
                type: "number"
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime59.jsx)(
              Field,
              {
                value: newPrice.cachedOutput,
                onChange: (cachedOutput) => setNewPrice({ ...newPrice, cachedOutput }),
                placeholder: "\u7F13\u5B58\u8F93\u51FA\u4EF7/\u767E\u4E07\uFF08\u53EF\u7559\u7A7A\uFF09",
                label: "\u7F13\u5B58\u8F93\u51FA\u4EF7/\u767E\u4E07\uFF08\u53EF\u7559\u7A7A\uFF09",
                type: "number"
              }
            )
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("div", { children: /* @__PURE__ */ (0, import_jsx_runtime59.jsx)(
            GlassButton,
            {
              size: "sm",
              onClick: () => setPriceNote(
                newPrice.model.trim().length === 0 ? "\u5148\u586B\u6A21\u578B\u540D\uFF08\u8981\u548C\u8BF7\u6C42\u91CC\u7528\u7684\u6A21\u578B\u540D\u4E00\u81F4\uFF09\u3002" : ""
              ),
              children: "\u4FDD\u5B58\u5355\u4EF7"
            }
          ) }),
          priceNote ? /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("p", { style: caption6, children: priceNote }) : null
        ] })
      ] }) : null
    ] })
  ] });
  return /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime59.jsx)(
      PageHeader,
      {
        variant: "ground",
        eyebrow: "// USAGE",
        title: "\u7528\u91CF\u7EDF\u8BA1",
        subtitle: "\u6BCF\u6B21\u8BF7\u6C42\u7684\u771F\u5B9E token \u8BB0\u5728\u672C\u5730\u8D26\u4E0A\uFF1A\u7F13\u5B58\u547D\u4E2D\u7684\u8F93\u5165\u6309\u7F13\u5B58\u4EF7\u91CD\u7B97\uFF0C\u601D\u8003/\u7F13\u5B58\u8F93\u51FA\u53E6\u8BA1\u4E00\u7B14\u3002",
        right: /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)(import_jsx_runtime59.Fragment, { children: [
          /* @__PURE__ */ (0, import_jsx_runtime59.jsx)(GlassButton, { size: "sm", children: "\u5237\u65B0" }),
          /* @__PURE__ */ (0, import_jsx_runtime59.jsx)(GlassButton, { size: "sm", children: "\u5BFC\u51FA CSV" }),
          /* @__PURE__ */ (0, import_jsx_runtime59.jsx)(TextBtn, { onClick: back, children: "\u8FD4\u56DE" })
        ] })
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("main", { className: "screen", children: /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)(
      "div",
      {
        style: {
          display: "grid",
          gridTemplateColumns: wide ? "1.7fr 1fr" : "minmax(0, 1fr)",
          gap: "var(--w-space-3)",
          alignItems: "start"
        },
        children: [
          left,
          right
        ]
      }
    ) })
  ] });
}
var TABLE_HEADS = ["\u540D\u79F0", "\u8C03\u7528", "\u8F93\u5165", "\u8F93\u51FA", "\u7F13\u5B58\u8F93\u5165", "\u7F13\u5B58\u8F93\u51FA", "\u6210\u672C"];
var TABLE_ROW = {
  display: "grid",
  gridTemplateColumns: "1.6fr 0.6fr 0.8fr 0.8fr 0.9fr 0.9fr 0.8fr",
  gap: "var(--w-space-1)",
  padding: "8px 12px"
};
function HeadBlock({ label, head, meta, sub }) {
  return /* @__PURE__ */ (0, import_jsx_runtime59.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 2, minWidth: 0 }, children: [
    /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("span", { style: caption6, children: label }),
    /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("span", { style: display, children: head }),
    /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("span", { style: { ...bodySm2, overflow: "hidden", whiteSpace: "nowrap", textOverflow: "ellipsis" }, children: meta }),
    sub ? /* @__PURE__ */ (0, import_jsx_runtime59.jsx)("span", { style: { ...caption6, overflow: "hidden", whiteSpace: "nowrap", textOverflow: "ellipsis" }, children: sub }) : null
  ] });
}

// src/screens/settings-misc/index.tsx
var screens8 = {
  usagestats: Usagestats,
  cloudsaves: Cloudsaves,
  colorpicker: Colorpicker,
  mentalHealth: MentalHealth,
  petsettings: Petsettings,
  petstudio: Petstudio
};

// src/screens/story/chatimport.tsx
var import_react46 = __toESM(require_react(), 1);

// src/screens/story/parts.tsx
var import_jsx_runtime60 = __toESM(require_jsx_runtime(), 1);
function Stack2({
  gap = 3,
  children,
  style
}) {
  return /* @__PURE__ */ (0, import_jsx_runtime60.jsx)("div", { style: { display: "flex", flexDirection: "column", gap: `var(--w-space-${gap})`, ...style }, children });
}
function Lead2({ eyebrow: eyebrow2, title: title2, sub }) {
  return /* @__PURE__ */ (0, import_jsx_runtime60.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-1)" }, children: [
    eyebrow2 ? /* @__PURE__ */ (0, import_jsx_runtime60.jsx)("span", { className: "eyebrow", children: eyebrow2 }) : null,
    /* @__PURE__ */ (0, import_jsx_runtime60.jsx)(
      "h2",
      {
        style: {
          margin: 0,
          fontSize: "var(--w-size-display)",
          fontWeight: 600,
          color: "var(--w-ink-display)",
          lineHeight: 1.35
        },
        children: title2
      }
    ),
    sub ? /* @__PURE__ */ (0, import_jsx_runtime60.jsx)("p", { style: { margin: 0, fontSize: "var(--w-size-small)", color: "var(--w-ink-muted)", lineHeight: 1.6 }, children: sub }) : null
  ] });
}
function Actions2({ children, end }) {
  return /* @__PURE__ */ (0, import_jsx_runtime60.jsx)(
    "div",
    {
      style: {
        display: "flex",
        flexWrap: "wrap",
        alignItems: "center",
        justifyContent: end ? "flex-end" : "flex-start",
        gap: "var(--w-space-2)"
      },
      children
    }
  );
}
function FootBar({ children }) {
  return /* @__PURE__ */ (0, import_jsx_runtime60.jsx)(
    Glass,
    {
      as: "div",
      tone: "bar",
      radius: 0,
      style: { position: "sticky", bottom: 0, zIndex: 5, borderTop: "1px solid var(--w-hairline)" },
      children: /* @__PURE__ */ (0, import_jsx_runtime60.jsx)(
        "div",
        {
          style: {
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            gap: "var(--w-space-3)",
            padding: "var(--w-space-2) var(--w-pad-page)"
          },
          children
        }
      )
    }
  );
}
function Note2({ children, warn }) {
  return /* @__PURE__ */ (0, import_jsx_runtime60.jsx)("p", { className: warn ? "cap cap--warn" : "cap", style: { margin: 0, lineHeight: 1.6 }, children });
}
function SectionTitle2({ children }) {
  return /* @__PURE__ */ (0, import_jsx_runtime60.jsx)("h3", { style: { margin: 0, fontSize: "var(--w-size-subhead)", fontWeight: 600, color: "var(--w-ink-display)" }, children });
}

// src/screens/story/chatimport.tsx
var import_jsx_runtime61 = __toESM(require_jsx_runtime(), 1);
function Chatimport({ navigate, back }) {
  const [pasted, setPasted] = (0, import_react46.useState)("");
  const [sources] = (0, import_react46.useState)([]);
  const [status, setStatus] = (0, import_react46.useState)("");
  const [error, setError] = (0, import_react46.useState)("");
  const [hasResult] = (0, import_react46.useState)(false);
  const characterDisplay = "\u8FD9\u4E00\u5C40";
  const pasteCount = pasted.trim() ? `${pasted.trim().length} \u5B57` : "";
  const parsePaste = () => {
    if (pasted.trim().length === 0) {
      setStatus("");
      setError("\u5148\u7C98\u8D34\u4E00\u6BB5\u804A\u5929\u8BB0\u5F55\uFF0C\u518D\u70B9\u89E3\u6790\u3002");
      return;
    }
    setError("");
  };
  return /* @__PURE__ */ (0, import_jsx_runtime61.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime61.jsx)(PageHeader, { title: "\u5BFC\u5165\u804A\u5929\u8BB0\u5F55", onBack: back, right: /* @__PURE__ */ (0, import_jsx_runtime61.jsx)("span", { className: "cap", children: characterDisplay }) }),
    /* @__PURE__ */ (0, import_jsx_runtime61.jsx)("div", { className: "screen", style: { maxWidth: 880 }, children: /* @__PURE__ */ (0, import_jsx_runtime61.jsxs)(Stack2, { gap: 4, children: [
      /* @__PURE__ */ (0, import_jsx_runtime61.jsx)(
        Lead2,
        {
          eyebrow: "01 \xB7 PAST",
          title: "\u628A\u4F60\u4EEC\u7684\u4EE5\u524D\u5E26\u8FDB\u6765",
          sub: "\u53EF\u4E00\u6B21\u9009\u591A\u4E2A\u6587\u4EF6\uFF0C\u4E5F\u53EF\u4EE5\u76F4\u63A5\u7C98\u4E00\u5927\u6BB5\u3002\u683C\u5F0F\u9760\u5185\u5BB9\u8BA4\uFF0C\u4E0D\u9760\u6269\u5C55\u540D\uFF1A\u5FAE\u4FE1/QQ \u5BFC\u51FA\u7684 txt/mht\u3001\u901A\u7528 txt\u3001md\u3001json\u3001csv \u90FD\u884C\u3002"
        }
      ),
      /* @__PURE__ */ (0, import_jsx_runtime61.jsxs)(Card, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime61.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime61.jsxs)(Stack2, { gap: 1, children: [
            /* @__PURE__ */ (0, import_jsx_runtime61.jsx)("span", { style: { fontSize: "var(--w-size-subhead)", color: "var(--w-ink-display)" }, children: "\u4ECE\u6587\u4EF6\u5BFC\u5165" }),
            /* @__PURE__ */ (0, import_jsx_runtime61.jsx)("span", { className: "cap", children: ".txt / .mht / .md / .json / .csv\uFF0C\u53EF\u591A\u9009\uFF1B\u8BFB\u4E0D\u51FA\u7684\u6587\u4EF6\u4F1A\u5355\u72EC\u8BF4\u539F\u56E0\uFF0C\u4E0D\u5F71\u54CD\u522B\u7684\u6587\u4EF6\u3002" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime61.jsx)(TextBtn, { children: "\u9009\u62E9\u6587\u4EF6\u2026" })
        ] }),
        sources.map((row) => /* @__PURE__ */ (0, import_jsx_runtime61.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 1 }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime61.jsx)("span", { style: { fontSize: "var(--w-size-small)", color: "var(--w-ink-body)" }, children: row.name }),
          /* @__PURE__ */ (0, import_jsx_runtime61.jsx)("span", { className: "cap", children: row.detail })
        ] }, row.name))
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime61.jsxs)(Card, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime61.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-2)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime61.jsxs)(Stack2, { gap: 1, children: [
            /* @__PURE__ */ (0, import_jsx_runtime61.jsx)("span", { style: { fontSize: "var(--w-size-subhead)", color: "var(--w-ink-display)" }, children: "\u6216\u8005\u76F4\u63A5\u7C98\u8D34" }),
            /* @__PURE__ */ (0, import_jsx_runtime61.jsx)("span", { className: "cap", children: "\u4E00\u6574\u6BB5\u804A\u5929\u8BB0\u5F55\u8D34\u8FDB\u6765\u5C31\u884C\uFF0C\u51E0\u5341\u4E07\u5B57\u4E5F\u80FD\u8BFB\u3002" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime61.jsx)("span", { className: "cap", children: pasteCount })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime61.jsx)(
          Field,
          {
            multiline: true,
            label: "\u7C98\u8D34\u7684\u804A\u5929\u8BB0\u5F55",
            value: pasted,
            onChange: setPasted,
            placeholder: "2024-05-01 20:14:00 \u665A\u665A\n\u4ECA\u5929\u4E0B\u96E8\u4E86\uFF0C\u4F60\u5E26\u4F1E\u4E86\u5417"
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime61.jsxs)(Actions2, { children: [
          /* @__PURE__ */ (0, import_jsx_runtime61.jsx)(SignalButton, { onClick: parsePaste, children: "\u89E3\u6790\u7C98\u8D34\u7684\u5185\u5BB9" }),
          /* @__PURE__ */ (0, import_jsx_runtime61.jsx)(TextBtn, { onClick: () => {
            setPasted("");
            setStatus("");
          }, children: "\u6E05\u7A7A" })
        ] })
      ] }),
      error ? /* @__PURE__ */ (0, import_jsx_runtime61.jsx)(Note2, { warn: true, children: error }) : null,
      hasResult && status ? /* @__PURE__ */ (0, import_jsx_runtime61.jsx)(Note2, { children: status }) : null,
      hasResult ? /* @__PURE__ */ (0, import_jsx_runtime61.jsx)(Lead2, { eyebrow: "02 \xB7 READ", title: "\u8BFB\u51FA\u6765\u7684\u6837\u5B50" }) : null
    ] }) }),
    hasResult ? /* @__PURE__ */ (0, import_jsx_runtime61.jsxs)(FootBar, { children: [
      /* @__PURE__ */ (0, import_jsx_runtime61.jsx)("span", { className: "cap", children: status }),
      /* @__PURE__ */ (0, import_jsx_runtime61.jsx)(SignalButton, { onClick: () => navigate("/newrun"), children: "\u5E26\u56DE\u8FD9\u4E00\u5C40" })
    ] }) : null
  ] });
}

// src/screens/story/forcecast.tsx
var import_react47 = __toESM(require_react(), 1);
var import_jsx_runtime62 = __toESM(require_jsx_runtime(), 1);
function Forcecast({ back }) {
  const [status, setStatus] = (0, import_react47.useState)("\u52FE\u9009\u8981\u70B9\u540D\u7684\u89D2\u8272\uFF0C\u5E76\u4E3A TA \u6307\u5B9A\u521D\u59CB\u4F4D\u7F6E");
  return /* @__PURE__ */ (0, import_jsx_runtime62.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime62.jsx)(PageHeader, { title: "\u70B9\u540D \xB7 \u8FDB\u5165\u5267\u60C5", onBack: back, right: /* @__PURE__ */ (0, import_jsx_runtime62.jsx)("span", { className: "cap", children: status }) }),
    /* @__PURE__ */ (0, import_jsx_runtime62.jsx)("div", { className: "screen", style: { minHeight: 360, justifyContent: "center" }, children: /* @__PURE__ */ (0, import_jsx_runtime62.jsx)(Empty, { title: "\u89D2\u8272\u5E93\u8FD8\u662F\u7A7A\u7684", reason: "\u5148\u521B\u5EFA\u51E0\u4E2A\u89D2\u8272\uFF0C\u518D\u6765\u70B9\u540D\u5427" }) }),
    /* @__PURE__ */ (0, import_jsx_runtime62.jsx)(FootBar, { children: /* @__PURE__ */ (0, import_jsx_runtime62.jsx)("div", { style: { width: "100%" }, children: /* @__PURE__ */ (0, import_jsx_runtime62.jsx)(Actions2, { children: /* @__PURE__ */ (0, import_jsx_runtime62.jsx)(SignalButton, { onClick: () => setStatus("\u8FD8\u6CA1\u6709\u52FE\u9009\u4EFB\u4F55\u89D2\u8272\u3002"), children: "\u786E\u5B9A\u70B9\u540D \xB7 \u8FDB\u5165\u5267\u60C5" }) }) }) })
  ] });
}

// src/screens/story/newrun.tsx
var import_react48 = __toESM(require_react(), 1);
var import_jsx_runtime63 = __toESM(require_jsx_runtime(), 1);
var STEP_NAMES = ["\u4E16\u754C", "\u4EBA\u8BBE", "\u7EC6\u5219", "\u5F00\u59CB"];
function Newrun({ query, navigate, back }) {
  const [step, setStep] = (0, import_react48.useState)(0);
  const [persona, setPersona] = (0, import_react48.useState)("");
  const [worldId, setWorldId] = (0, import_react48.useState)("");
  const [status, setStatus] = (0, import_react48.useState)("");
  const [hasImport] = (0, import_react48.useState)(false);
  const character = query.get("character") ?? "";
  const characterName = "TA";
  const pageTitle = "\u65B0\u7684\u4E00\u5C40";
  const selectedWorldDisplay = worldId ? "\u5DF2\u9009\u7684\u4E16\u754C" : "\u4E0D\u7ED1\u5B9A \xB7 \u666E\u901A\u9152\u9986";
  const mapHint = worldId ? "\u5730\u56FE\u53EF\u7528 \xB7 \u5730\u70B9\u6765\u81EA\u4E16\u754C\u4E66" : "\u5730\u56FE\u9501\u5B9A \xB7 \u8FD9\u4E00\u5C40\u53EA\u6709\u5BF9\u8BDD";
  const personaCount = persona.trim().length === 0 ? "\u7559\u7A7A\u4E5F\u53EF\u4EE5" : `${persona.trim().length} \u5B57`;
  const hasPersona = persona.trim().length > 0;
  const importHint = "\u53EF\u9009\u3002\u628A\u5FAE\u4FE1/QQ \u5BFC\u51FA\u7684\u804A\u5929\u8BB0\u5F55\u5E26\u8FDB\u6765\uFF0C\u8FD9\u4E00\u5C40\u7684\u5173\u7CFB\u4F1A\u4ECE\u4F60\u4EEC\u7684\u8FC7\u53BB\u5F00\u59CB\u3002";
  const begin = () => {
    if (!character) setStatus("\u6CA1\u6709\u53EF\u8FDB\u5165\u7684\u89D2\u8272\u3002");
  };
  return /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(
      PageHeader,
      {
        title: pageTitle,
        onBack: back,
        right: /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", { className: "cap", children: characterName })
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)("div", { className: "screen", style: { maxWidth: 880 }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)(Stack2, { gap: 2, children: [
        /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("div", { style: { display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: 6, height: 4 }, children: STEP_NAMES.map((name, index) => /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(
          "i",
          {
            style: {
              display: "block",
              borderRadius: 2,
              background: step >= index ? "var(--w-signal)" : "var(--w-hairline)"
            }
          },
          name
        )) }),
        /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr auto auto", alignItems: "center", gap: "var(--w-space-3)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", { className: "eyebrow", children: STEP_NAMES[step] }),
          step > 0 ? /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(TextBtn, { onClick: () => setStep(step - 1), children: "\u2190 \u4E0A\u4E00\u6B65" }) : /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", {}),
          /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", { className: "cap", style: { fontFamily: "var(--w-font-num)" }, children: `${step + 1} / 4` })
        ] })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)(Stack2, { gap: 4, children: [
        step === 0 ? /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)(Stack2, { gap: 3, children: [
          /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(Lead2, { eyebrow: "01 \xB7 WORLD", title: "\u8FD9\u6BB5\u5173\u7CFB\u53D1\u751F\u5728\u54EA\u4E2A\u4E16\u754C", sub: `\u548C\u300C${characterName}\u300D\u7684\u8FD9\u6BB5\u76F8\u5904\uFF0C\u4ECE\u54EA\u91CC\u5F00\u59CB\uFF1F` }),
          /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)(Stack2, { gap: 2, children: [
            /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
              /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", { style: { fontSize: "var(--w-size-subhead)", color: "var(--w-ink-display)" }, children: "\u4E16\u754C\u4E66" }),
              /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(TextBtn, { onClick: () => navigate("/worldbook"), children: "\u7BA1\u7406\u4E16\u754C\u4E66" })
            ] }),
            /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)(Card, { children: [
              /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(
                Row,
                {
                  title: /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)("span", { style: { display: "inline-flex", alignItems: "center", gap: "var(--w-space-2)" }, children: [
                    worldId === "" ? /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("i", { style: { width: 3, height: 24, borderRadius: 2, background: "var(--w-signal)" } }) : null,
                    "\u4E0D\u7ED1\u5B9A \xB7 \u666E\u901A\u9152\u9986"
                  ] }),
                  desc: "\u53EA\u804A\u5929\uFF0C\u6CA1\u6709\u5730\u56FE\u3002TA \u4E0D\u4F1A\u5047\u88C5\u77E5\u9053\u4F60\u4EEC\u5728\u54EA\u513F\u3002",
                  right: /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)("span", { style: { display: "flex", flexDirection: "column", alignItems: "flex-end", gap: 2 }, children: [
                    worldId === "" ? /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", { className: "cap", children: "\u5DF2\u9009" }) : null,
                    /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", { className: "cap", children: "\u65E0\u5730\u56FE" })
                  ] }),
                  onClick: () => setWorldId("")
                }
              ),
              /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(Sep, {}),
              /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("p", { className: "cap", style: { margin: 0 }, children: "\u8FD8\u6CA1\u6709\u4E16\u754C\u4E66\u3002\u5230\u300C\u4E16\u754C\u4E66\u300D\u91CC\u624B\u5199\u4E00\u4EFD\uFF0C\u6216\u8005\u8BA9 AI \u5E2E\u4F60\u9020\u4E00\u4E2A\u4E16\u754C\u3002" })
            ] }),
            /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(Note2, { children: "\u4E0D\u7ED1\u5B9A\u4E5F\u80FD\u5F00\u59CB\uFF1A\u90A3\u5C31\u662F\u666E\u901A\u9152\u9986\u2014\u2014\u53EA\u804A\u5929\uFF0C\u5730\u56FE\u4E0E\u79FB\u52A8\u4F1A\u9501\u4E0A\u3002" })
          ] })
        ] }) : null,
        step === 1 ? /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)(Stack2, { gap: 3, children: [
          /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(
            Lead2,
            {
              eyebrow: "02 \xB7 YOU",
              title: "\u4F60\u662F\u8C01",
              sub: "\u5199\u4F60\u5728\u8FD9\u6BB5\u5173\u7CFB\u91CC\u7684\u8EAB\u4EFD\uFF1A\u5DE5\u4F5C\u3001\u4E60\u60EF\u3001\u548C TA \u7684\u5173\u7CFB\u3001\u5DF2\u7ECF\u8BF4\u597D\u7684\u4E8B\u3002\u7559\u7A7A\u4E5F\u53EF\u4EE5\u3002"
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)(Card, { children: [
            /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
              /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", { className: "cap", style: { margin: 0 }, children: "\u4EBA\u8BBE" }),
              /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", { className: "cap", children: personaCount })
            ] }),
            /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(
              Field,
              {
                multiline: true,
                label: "\u4EBA\u8BBE",
                value: persona,
                onChange: setPersona,
                placeholder: "\u6BD4\u5982\uFF1A\u6211\u53EB\u6797\u665A\uFF0C\u505A\u5EFA\u7B51\u8BBE\u8BA1\uFF0C\u548C TA \u5DF2\u7ECF\u7ED3\u5A5A\u4E24\u5E74\u3002\u6015\u51B7\uFF0C\u65E9\u4E0A\u4E00\u5B9A\u8981\u559D\u4E00\u676F\u70ED\u8336\uFF1B\u4E0D\u559C\u6B22\u88AB\u4E34\u65F6\u6539\u8BA1\u5212\u3002"
              }
            )
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(Note2, { children: "\u4EBA\u8BBE\u4E0D\u4F1A\u6574\u6BB5\u585E\u8FDB\u4E0A\u4E0B\u6587\uFF1A\u5B83\u4F1A\u88AB\u6574\u7406\u6210 TA \u672C\u6765\u5C31\u77E5\u9053\u7684\u8BB0\u5FC6\uFF0C\u9700\u8981\u7684\u65F6\u5019\u624D\u60F3\u8D77\u6765\u3002" })
        ] }) : null,
        /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)(Card, { children: [
          /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-2)" }, children: [
            /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)(Stack2, { gap: 1, children: [
              /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", { style: { fontSize: "var(--w-size-subhead)", color: "var(--w-ink-display)" }, children: "\u4ECE\u804A\u5929\u8BB0\u5F55\u5F00\u59CB" }),
              /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", { className: "cap", children: importHint })
            ] }),
            /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(TextBtn, { onClick: () => navigate(character ? `/chatimport?character=${encodeURIComponent(character)}` : "/chatimport"), children: "\u5BFC\u5165\u2026" })
          ] }),
          hasImport ? /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(TextBtn, { children: "\u4E0D\u5E26\u8FD9\u4EFD\u8BB0\u5F55" }) : null
        ] }),
        step === 2 ? /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)(Stack2, { gap: 3, children: [
          /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(
            Lead2,
            {
              eyebrow: "03 \xB7 DETAILS",
              title: "\u5F00\u5C40\u7EC6\u5219",
              sub: "\u6CA1\u6709\u989D\u5916\u8981\u586B\u7684\uFF1A\u8FD9\u4E00\u9875\u53EA\u628A\u5DF2\u7ECF\u9009\u597D\u7684\u4E1C\u897F\u6446\u6E05\u695A\u3002\u4E4B\u540E\u4E5F\u80FD\u968F\u65F6\u4ECE\u4E3B\u754C\u9762\u56DE\u6765\u6539\u3002"
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)(Card, { children: [
            /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(
              Row,
              {
                title: /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)("span", { style: { display: "inline-flex", alignItems: "center", gap: "var(--w-space-2)" }, children: [
                  /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", { className: "cap", style: { minWidth: 48 }, children: "TA" }),
                  characterName
                ] }),
                right: /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", { className: "cap", children: "\u8FD9\u4E00\u5C40\u7684\u4E3B\u89D2" })
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(Sep, {}),
            /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(
              Row,
              {
                title: /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)("span", { style: { display: "inline-flex", alignItems: "center", gap: "var(--w-space-2)" }, children: [
                  /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", { className: "cap", style: { minWidth: 48 }, children: "\u4E16\u754C" }),
                  /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)("span", { style: { display: "flex", flexDirection: "column", gap: 2 }, children: [
                    /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", { children: selectedWorldDisplay }),
                    /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", { className: "cap", children: mapHint })
                  ] })
                ] }),
                right: /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(TextBtn, { onClick: () => setStep(0), children: "\u6539" })
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(Sep, {}),
            /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(
              Row,
              {
                title: /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)("span", { style: { display: "inline-flex", alignItems: "center", gap: "var(--w-space-2)" }, children: [
                  /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", { className: "cap", style: { minWidth: 48 }, children: "\u4EBA\u8BBE" }),
                  personaCount
                ] }),
                right: /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(TextBtn, { onClick: () => setStep(1), children: "\u53BB\u5199" })
              }
            )
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(Note2, { children: "\u5F00\u59CB\u524D\u4F1A\u5148\u6574\u7406\u4EBA\u683C\u8BB0\u5FC6\uFF0C\u518D\u843D\u76D8\uFF1B\u6574\u7406\u597D\u7684\u8BB0\u5FC6\u4E0D\u5360\u7528\u4E4B\u540E\u7684\u6BCF\u4E00\u8F6E\u4E0A\u4E0B\u6587\u3002" })
        ] }) : null,
        step === 3 ? /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)(Stack2, { gap: 3, children: [
          /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(
            Lead2,
            {
              eyebrow: "04 \xB7 BEGIN",
              title: "\u5C31\u8FD9\u6837\u5F00\u59CB",
              sub: "\u770B\u4E00\u773C\u5C31\u597D\u3002\u5F00\u59CB\u4E4B\u540E\uFF0C\u8FD9\u4E2A\u4E16\u754C\u4E0E\u4F60\u7684\u4EBA\u8BBE\u4F1A\u5148\u53D8\u6210 TA \u7684\u8BB0\u5FC6\u3002"
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)(Card, { children: [
            /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(
              Row,
              {
                title: /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)("span", { style: { display: "inline-flex", alignItems: "center", gap: "var(--w-space-2)" }, children: [
                  /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", { className: "cap", style: { minWidth: 48 }, children: "\u4E0E\u8C01" }),
                  characterName
                ] })
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(Sep, {}),
            /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(
              Row,
              {
                title: /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)("span", { style: { display: "inline-flex", alignItems: "center", gap: "var(--w-space-2)" }, children: [
                  /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", { className: "cap", style: { minWidth: 48 }, children: "\u4E16\u754C" }),
                  /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)("span", { style: { display: "flex", flexDirection: "column", gap: 2 }, children: [
                    /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", { children: selectedWorldDisplay }),
                    /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", { className: "cap", children: mapHint })
                  ] })
                ] })
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(Sep, {}),
            /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(
              Row,
              {
                title: /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)("span", { style: { display: "inline-flex", alignItems: "flex-start", gap: "var(--w-space-2)" }, children: [
                  /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", { className: "cap", style: { minWidth: 48 }, children: "\u4EBA\u8BBE" }),
                  hasPersona ? /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", { style: { fontSize: "var(--w-size-small)", color: "var(--w-ink-body)", whiteSpace: "pre-wrap" }, children: persona }) : /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", { className: "cap", children: "\u8FD8\u6CA1\u5199\u3002\u5F00\u59CB\u540E TA \u4F1A\u5148\u6309\u4E16\u754C\u4E66\u4E0E\u5DF2\u6709\u7684\u8BB0\u5FC6\u8BA4\u8BC6\u4F60\u3002" })
                ] })
              }
            )
          ] })
        ] }) : null,
        status ? /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(Note2, { warn: true, children: status }) : null
      ] })
    ] }),
    /* @__PURE__ */ (0, import_jsx_runtime63.jsxs)(FootBar, { children: [
      /* @__PURE__ */ (0, import_jsx_runtime63.jsx)("span", { className: "cap", children: selectedWorldDisplay }),
      /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(Actions2, { children: step < 3 ? /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(SignalButton, { onClick: () => setStep(step + 1), children: "\u4E0B\u4E00\u6B65" }) : /* @__PURE__ */ (0, import_jsx_runtime63.jsx)(SignalButton, { onClick: begin, children: "\u5F00\u59CB\u8FD9\u4E00\u5C40" }) })
    ] })
  ] });
}

// src/screens/story/novelselect.tsx
var import_react49 = __toESM(require_react(), 1);
var import_jsx_runtime64 = __toESM(require_jsx_runtime(), 1);
function NovelSelect({ back }) {
  const [novels] = (0, import_react49.useState)([]);
  return /* @__PURE__ */ (0, import_jsx_runtime64.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime64.jsx)(PageHeader, { title: "\u5E73\u884C\u5B87\u5B99", onBack: back }),
    /* @__PURE__ */ (0, import_jsx_runtime64.jsxs)("div", { className: "screen", style: { minHeight: 360 }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime64.jsx)(Card, { children: novels.length === 0 ? /* @__PURE__ */ (0, import_jsx_runtime64.jsx)("div", { style: { display: "flex", alignItems: "center", justifyContent: "center", minHeight: 260 }, children: /* @__PURE__ */ (0, import_jsx_runtime64.jsx)(Empty, { title: "\u8FD8\u6CA1\u6709\u5E73\u884C\u5B87\u5B99", reason: "\u5BFC\u5165\u4E00\u90E8\u5C0F\u8BF4\uFF0C\u5B83\u5C31\u4F1A\u53D8\u6210\u4E00\u4E2A\u53EF\u4EE5\u8FDB\u5165\u7684\u4E16\u754C" }) }) : novels.map((novel) => /* @__PURE__ */ (0, import_jsx_runtime64.jsxs)("div", { style: { display: "flex", flexDirection: "column", gap: 3 }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime64.jsx)("span", { style: { fontSize: "var(--w-size-body)", color: "var(--w-ink-display)" }, children: novel.title }),
        /* @__PURE__ */ (0, import_jsx_runtime64.jsx)("span", { className: "cap", children: novel.summary }),
        /* @__PURE__ */ (0, import_jsx_runtime64.jsx)("span", { className: "cap", children: novel.charactersLabel })
      ] }, novel.id)) }),
      /* @__PURE__ */ (0, import_jsx_runtime64.jsx)(Stack2, { children: /* @__PURE__ */ (0, import_jsx_runtime64.jsx)("div", { style: { display: "grid", gridTemplateColumns: "1fr" }, children: /* @__PURE__ */ (0, import_jsx_runtime64.jsx)(GlassButton, { size: "lg", children: "\u5BFC\u5165\u5C0F\u8BF4\uFF0C\u521B\u5EFA\u65B0\u4E16\u754C" }) }) })
    ] })
  ] });
}

// src/screens/story/novelworld.tsx
var import_react50 = __toESM(require_react(), 1);
var import_jsx_runtime65 = __toESM(require_jsx_runtime(), 1);
function Novelworld({ query, back }) {
  const [input, setInput] = (0, import_react50.useState)("");
  const [messages, setMessages] = (0, import_react50.useState)([]);
  const novelId = query.get("id");
  const send = () => {
    const text = input;
    if (!text.trim()) return;
    setInput("");
    setMessages((list) => [...list, { text, mine: true }]);
    if (!novelId) return;
  };
  return /* @__PURE__ */ (0, import_jsx_runtime65.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime65.jsx)(PageHeader, { title: "\u5E73\u884C\u5B87\u5B99", onBack: back }),
    /* @__PURE__ */ (0, import_jsx_runtime65.jsx)("div", { className: "screen", style: { maxWidth: 960 }, children: /* @__PURE__ */ (0, import_jsx_runtime65.jsx)("div", { style: { display: "flex", flexDirection: "column", gap: "var(--w-space-1)" }, children: messages.map((message, index) => /* @__PURE__ */ (0, import_jsx_runtime65.jsx)("div", { style: { display: "flex", justifyContent: message.mine ? "flex-end" : "flex-start" }, children: /* @__PURE__ */ (0, import_jsx_runtime65.jsx)(
      "div",
      {
        style: {
          maxWidth: 480,
          padding: "var(--w-space-2) var(--w-space-3)",
          borderRadius: "var(--w-radius-well)",
          background: message.mine ? "var(--w-tint-button)" : "var(--w-tint-card)",
          color: "var(--w-ink-body)",
          fontSize: "var(--w-size-body)",
          lineHeight: 1.6,
          whiteSpace: "pre-wrap"
        },
        children: message.text
      }
    ) }, index)) }) }),
    /* @__PURE__ */ (0, import_jsx_runtime65.jsx)(
      Glass,
      {
        as: "div",
        tone: "bar",
        radius: 0,
        style: { position: "sticky", bottom: 0, zIndex: 5, borderTop: "1px solid var(--w-hairline)" },
        children: /* @__PURE__ */ (0, import_jsx_runtime65.jsxs)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-2)", padding: "var(--w-space-2) var(--w-pad-page)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime65.jsx)(
            "div",
            {
              style: {
                flex: 1,
                display: "flex",
                alignItems: "center",
                padding: "0 10px",
                borderRadius: "var(--w-radius-well)",
                background: "var(--w-surface-tint)"
              },
              children: /* @__PURE__ */ (0, import_jsx_runtime65.jsx)(Field, { label: "\u5BF9\u8FD9\u4E2A\u4E16\u754C\u8BF4\u70B9\u4EC0\u4E48", value: input, onChange: setInput, placeholder: "\u5BF9\u8FD9\u4E2A\u4E16\u754C\u8BF4\u70B9\u4EC0\u4E48\u2026" })
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime65.jsx)(SignalButton, { onClick: send, children: "\u53D1\u9001" })
        ] })
      }
    )
  ] });
}

// src/screens/story/SceneCalibrationPage.tsx
var import_react51 = __toESM(require_react(), 1);
var import_jsx_runtime66 = __toESM(require_jsx_runtime(), 1);
var LANDING_NAME = { point: "\u70B9\u9009", full: "\u6574\u56FE\u53EF\u7AD9", linear: "\u7EBF\u6027" };
var SCALE_NAME = { dualLine: "\u53CC\u7EBF", spheres: "\u5706\u7403" };
function SceneCalibrationPage({ back }) {
  const [landing, setLanding] = (0, import_react51.useState)("point");
  const [scale, setScale] = (0, import_react51.useState)("spheres");
  const [status, setStatus] = (0, import_react51.useState)("\u62D6\u7A7A\u767D\u5904\u5F00\u59CB\u6807\u5B9A");
  const summary = `\u2460 \u843D\u811A\u70B9\u7C7B\u578B\uFF1A${LANDING_NAME[landing]}` + (landing === "point" ? "\uFF080 \u4E2A\u70B9\uFF09" : landing === "full" ? "\uFF08\u6574\u5F20\u56FE\u5747\u53EF\u7AD9\u7ACB\uFF0C\u65E0\u843D\u811A\u51E0\u4F55\uFF09" : "\uFF080 \u6761\u7EBF\uFF0C\u603B\u957F 0\uFF09") + "\n\u2461 \u7F29\u653E\u6807\u5B9A\u65B9\u5F0F\uFF1A" + SCALE_NAME[scale] + "\n\u2462 \u6807\u5B9A\u6570\u636E\uFF1A" + (scale === "dualLine" ? "\u53CC\u7EBF\u6570\u636E\u4E0D\u53EF\u7528" : "0 \u4E2A\u7403\uFF0C\u534A\u5F84 []\uFF0C\u57FA\u51C6\u534A\u5F84 0\uFF0C\u6574\u4F53\u7CFB\u6570 1\uFF0C\u53C2\u8003\u8EAB\u9AD8\u5360\u6BD4 0.5");
  const missing = [];
  if (landing === "point") missing.push("\u843D\u811A\u70B9\u672A\u6807\u5B9A\uFF1A\u70B9\u9009\u6A21\u5F0F\u81F3\u5C11\u8981\u6307\u5B9A 1 \u4E2A\u843D\u811A\u70B9");
  if (landing === "linear") missing.push("\u843D\u811A\u70B9\u672A\u6807\u5B9A\uFF1A\u7EBF\u6027\u6A21\u5F0F\u81F3\u5C11\u8981\u753B 1 \u6761\u53EF\u7AD9\u7ACB\u7684\u7EBF");
  if (scale === "dualLine") missing.push("\u7F29\u653E\u6807\u5B9A\u672A\u5B8C\u6210\uFF1A\u53CC\u7EBF\u65B9\u5F0F\u9700\u8981\u6070\u597D 2 \u6761\u7EBF\uFF0C\u5F53\u524D 0 \u6761");
  if (scale === "spheres") missing.push("\u7F29\u653E\u6807\u5B9A\u672A\u5B8C\u6210\uFF1A\u5706\u7403\u65B9\u5F0F\u81F3\u5C11\u9700\u8981 2 \u4E2A\u5706\u7403\uFF08\u8FD1\u5904\u4E00\u4E2A\u3001\u8FDC\u5904\u4E00\u4E2A\uFF09\uFF0C\u5F53\u524D 0 \u4E2A");
  const chip = (active, label) => active ? `\u25CF ${label}` : label;
  return /* @__PURE__ */ (0, import_jsx_runtime66.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime66.jsx)(
      PageHeader,
      {
        title: "\u6807\u5B9A\u80CC\u666F\uFF1A\u672A\u547D\u540D\u573A\u666F",
        subtitle: "\u539F\u56FE\u5C3A\u5BF8\u672A\u77E5\uFF08\u89C6\u9891\u80CC\u666F\u53EF\u80FD\u5982\u6B64\uFF09",
        right: /* @__PURE__ */ (0, import_jsx_runtime66.jsxs)(import_jsx_runtime66.Fragment, { children: [
          /* @__PURE__ */ (0, import_jsx_runtime66.jsx)("span", { className: "cap", children: status }),
          /* @__PURE__ */ (0, import_jsx_runtime66.jsx)(TextBtn, { onClick: back, children: "\u53D6\u6D88" })
        ] })
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime66.jsx)(Glass, { as: "div", tone: "bar", radius: 0, children: /* @__PURE__ */ (0, import_jsx_runtime66.jsxs)(Stack2, { gap: 2, style: { padding: "var(--w-space-2) var(--w-pad-page)" }, children: [
      /* @__PURE__ */ (0, import_jsx_runtime66.jsxs)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-2)", flexWrap: "wrap" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime66.jsx)("span", { className: "cap", style: { width: 60 }, children: "\u843D\u811A\u70B9" }),
        /* @__PURE__ */ (0, import_jsx_runtime66.jsx)(TextBtn, { onClick: () => {
          if (landing !== "point") {
            setLanding("point");
            setStatus("\u843D\u811A\u70B9\u7C7B\u578B\uFF1A\u70B9\u9009");
          }
        }, children: chip(landing === "point", LANDING_NAME.point) }),
        /* @__PURE__ */ (0, import_jsx_runtime66.jsx)(TextBtn, { onClick: () => {
          if (landing !== "full") {
            setLanding("full");
            setStatus("\u843D\u811A\u70B9\u7C7B\u578B\uFF1A\u6574\u56FE\u53EF\u7AD9");
          }
        }, children: chip(landing === "full", LANDING_NAME.full) }),
        /* @__PURE__ */ (0, import_jsx_runtime66.jsx)(TextBtn, { onClick: () => {
          if (landing !== "linear") {
            setLanding("linear");
            setStatus("\u843D\u811A\u70B9\u7C7B\u578B\uFF1A\u7EBF\u6027");
          }
        }, children: chip(landing === "linear", LANDING_NAME.linear) })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime66.jsxs)("div", { style: { display: "flex", alignItems: "center", gap: "var(--w-space-2)", flexWrap: "wrap" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime66.jsx)("span", { className: "cap", style: { width: 60 }, children: "\u7F29\u653E" }),
        /* @__PURE__ */ (0, import_jsx_runtime66.jsx)(TextBtn, { onClick: () => {
          if (scale !== "dualLine") {
            setScale("dualLine");
            setStatus("\u7F29\u653E\u6807\u5B9A\u65B9\u5F0F\uFF1A\u53CC\u7EBF");
          }
        }, children: chip(scale === "dualLine", SCALE_NAME.dualLine) }),
        /* @__PURE__ */ (0, import_jsx_runtime66.jsx)(TextBtn, { onClick: () => {
          if (scale !== "spheres") {
            setScale("spheres");
            setStatus("\u7F29\u653E\u6807\u5B9A\u65B9\u5F0F\uFF1A\u5706\u7403");
          }
        }, children: chip(scale === "spheres", SCALE_NAME.spheres) }),
        /* @__PURE__ */ (0, import_jsx_runtime66.jsx)(TextBtn, { onClick: () => setStatus("\u9700\u8981\u6070\u597D\u4E24\u6761\u7F29\u653E\u7EBF\u624D\u80FD\u7FFB\u8F6C"), children: "\u21C4 \u7FFB\u8F6C\u7B2C\u4E8C\u6761\u7EBF" }),
        /* @__PURE__ */ (0, import_jsx_runtime66.jsx)(TextBtn, { children: "\u21BB \u9002\u5E94\u7A97\u53E3" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime66.jsx)("span", { className: "cap", children: "\u62D6\u7A7A\u767D=\u65B0\u5EFA \xB7 \u62D6\u9876\u70B9/\u7403\u5FC3=\u79FB\u52A8 \xB7 \u62D6\u7403\u7F18=\u6539\u534A\u5F84 \xB7 \u53CC\u51FB=\u5220\u9664\uFF08\u7EBF\u8EAB\u53CC\u51FB=\u63D2\u6298\u70B9\uFF09 \xB7 \u53F3\u952E=\u64A4\u9500 \xB7 \u6EDA\u8F6E/\u53CC\u6307=\u7F29\u653E \xB7 \u9009\u300C\u5E73\u79FB\u300D\u540E\u53EF\u62D6\u52A8\u753B\u9762" })
    ] }) }),
    /* @__PURE__ */ (0, import_jsx_runtime66.jsx)(
      "div",
      {
        style: {
          height: 380,
          margin: "0 var(--w-pad-page)",
          borderRadius: "var(--w-radius-well)",
          background: "var(--w-ink-display)"
        },
        "aria-hidden": "true"
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime66.jsx)("div", { className: "screen", style: { paddingTop: 0 }, children: /* @__PURE__ */ (0, import_jsx_runtime66.jsxs)(Card, { children: [
      /* @__PURE__ */ (0, import_jsx_runtime66.jsx)("p", { style: { margin: 0, fontSize: "var(--w-size-small)", color: "var(--w-ink-body)", whiteSpace: "pre-wrap", lineHeight: 1.6 }, children: summary }),
      missing.length > 0 ? /* @__PURE__ */ (0, import_jsx_runtime66.jsx)("p", { style: { margin: 0, fontSize: "var(--w-size-small)", color: "var(--w-signal)", whiteSpace: "pre-wrap", lineHeight: 1.6 }, children: `\u8FD8\u5DEE\uFF1A
${missing.map((m, i) => `${i + 1}. ${m}`).join("\n")}` }) : null,
      /* @__PURE__ */ (0, import_jsx_runtime66.jsxs)(Actions2, { end: true, children: [
        /* @__PURE__ */ (0, import_jsx_runtime66.jsx)(TextBtn, { onClick: () => setStatus("\u5DF2\u6E05\u7A7A\u6240\u6709\u6807\u5B9A\u51E0\u4F55"), children: "\u6E05\u7A7A\u91CD\u6807" }),
        /* @__PURE__ */ (0, import_jsx_runtime66.jsx)(SignalButton, { disabled: missing.length > 0, children: "\u5B8C\u6210\u6807\u5B9A" })
      ] })
    ] }) })
  ] });
}

// src/screens/story/scenelibrary.tsx
var import_react52 = __toESM(require_react(), 1);
var import_jsx_runtime67 = __toESM(require_jsx_runtime(), 1);
function newMode(name) {
  return { id: `m${Date.now()}${Math.random().toString(16).slice(2, 6)}`, name, detail: "\u706F\u5149 normal\uFF5C\u4E0D\u9650\u65F6\u95F4\uFF5C\u80CC\u666F\u6CBF\u7528\u6761\u76EE\uFF5C\u4E0D\u6539\u97F3\u4E50" };
}
function Scenelibrary({ back }) {
  const [entries, setEntries] = (0, import_react52.useState)([]);
  const [selectedId, setSelectedId] = (0, import_react52.useState)(null);
  const [selectedModeId, setSelectedModeId] = (0, import_react52.useState)(null);
  const [stateText] = (0, import_react52.useState)("\u5F53\u524D\u65E0\u751F\u6548\u573A\u666F\u3002\u65F6\u95F4\u547D\u4E2D\u6216 AI \u5207\u6362\u540E\u4F1A\u663E\u793A\u5728\u8FD9\u91CC\u3002");
  const selected = entries.find((e) => e.id === selectedId) ?? null;
  const selectedMode = selected?.modes.find((m) => m.id === selectedModeId) ?? null;
  const addEntry = () => {
    const entry = { id: `e${Date.now()}`, name: "\u672A\u547D\u540D\u573A\u666F", mapSceneRef: "", modes: [] };
    setEntries((list) => [...list, entry]);
    setSelectedId(entry.id);
    setSelectedModeId(null);
  };
  const renameEntry = () => {
    if (!selected) return;
    const name = window.prompt("\u91CD\u547D\u540D", selected.name);
    if (!name || !name.trim()) return;
    setEntries((list) => list.map((e) => e.id === selected.id ? { ...e, name: name.trim() } : e));
  };
  const removeEntry = () => {
    if (!selected) return;
    if (!window.confirm(`\u786E\u5B9A\u5220\u9664\u300C${selected.name}\u300D\u53CA\u5176\u5168\u90E8\u6A21\u5F0F\u5417\uFF1F`)) return;
    setEntries((list) => list.filter((e) => e.id !== selected.id));
    setSelectedId(null);
    setSelectedModeId(null);
  };
  const addMode = () => {
    if (!selected) return;
    const mode = newMode("\u65B0\u6A21\u5F0F");
    setEntries((list) => list.map((e) => e.id === selected.id ? { ...e, modes: [...e.modes, mode] } : e));
    setSelectedModeId(mode.id);
  };
  const renameMode = () => {
    if (!selected || !selectedMode) return;
    const name = window.prompt("\u6A21\u5F0F\u547D\u540D", selectedMode.name);
    if (!name || !name.trim()) return;
    setEntries(
      (list) => list.map(
        (e) => e.id === selected.id ? { ...e, modes: e.modes.map((m) => m.id === selectedMode.id ? { ...m, name: name.trim() } : m) } : e
      )
    );
  };
  const removeMode = () => {
    if (!selected || !selectedMode) return;
    if (!window.confirm(`\u786E\u5B9A\u5220\u9664\u6A21\u5F0F\u300C${selectedMode.name}\u300D\u5417\uFF1F`)) return;
    setEntries(
      (list) => list.map(
        (e) => e.id === selected.id ? { ...e, modes: e.modes.filter((m) => m.id !== selectedMode.id) } : e
      )
    );
    setSelectedModeId(null);
  };
  const modeBtn = (label, onClick) => /* @__PURE__ */ (0, import_jsx_runtime67.jsx)(TextBtn, { onClick, disabled: !selectedMode, children: label });
  return /* @__PURE__ */ (0, import_jsx_runtime67.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime67.jsx)(
      PageHeader,
      {
        title: "\u573A\u666F\u5DE5\u574A",
        onBack: back,
        right: /* @__PURE__ */ (0, import_jsx_runtime67.jsx)(SignalButton, { onClick: addEntry, children: "\uFF0B \u65B0\u5EFA" })
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime67.jsxs)("div", { className: "screen", children: [
      /* @__PURE__ */ (0, import_jsx_runtime67.jsxs)(Stack2, { gap: 4, children: [
        /* @__PURE__ */ (0, import_jsx_runtime67.jsx)(Card, { children: entries.map((entry) => /* @__PURE__ */ (0, import_jsx_runtime67.jsxs)("div", { children: [
          /* @__PURE__ */ (0, import_jsx_runtime67.jsx)(
            Row,
            {
              title: entry.name,
              desc: `\u5730\u56FE\u5F15\u7528\uFF1A${entry.mapSceneRef}`,
              right: /* @__PURE__ */ (0, import_jsx_runtime67.jsx)(TextBtn, { onClick: () => {
                setSelectedId(entry.id);
                setSelectedModeId(entry.modes[0]?.id ?? null);
              }, children: "\u5E94\u7528" })
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime67.jsx)(Sep, {})
        ] }, entry.id)) }),
        selected ? /* @__PURE__ */ (0, import_jsx_runtime67.jsxs)(Card, { children: [
          /* @__PURE__ */ (0, import_jsx_runtime67.jsxs)(Actions2, { children: [
            /* @__PURE__ */ (0, import_jsx_runtime67.jsx)(TextBtn, { onClick: renameEntry, children: "\u91CD\u547D\u540D" }),
            /* @__PURE__ */ (0, import_jsx_runtime67.jsx)(TextBtn, { children: "\u5F15\u7528\u5730\u56FE\u573A\u666F" }),
            /* @__PURE__ */ (0, import_jsx_runtime67.jsx)(TextBtn, { children: "\u53D8\u6362\u89C4\u5219" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime67.jsxs)(Actions2, { children: [
            /* @__PURE__ */ (0, import_jsx_runtime67.jsx)(TextBtn, { onClick: addMode, children: "\uFF0B \u52A0\u6A21\u5F0F" }),
            /* @__PURE__ */ (0, import_jsx_runtime67.jsx)(TextBtn, { onClick: removeEntry, children: "\u5220\u9664\u6761\u76EE" })
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime67.jsx)("div", { style: { maxHeight: 132, overflowY: "auto" }, children: selected.modes.map((mode) => /* @__PURE__ */ (0, import_jsx_runtime67.jsxs)("div", { children: [
            /* @__PURE__ */ (0, import_jsx_runtime67.jsx)(
              Row,
              {
                title: mode.name,
                desc: mode.detail,
                right: /* @__PURE__ */ (0, import_jsx_runtime67.jsx)(TextBtn, { onClick: () => setSelectedModeId(mode.id), children: "\u5E94\u7528" })
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime67.jsx)(Sep, {})
          ] }, mode.id)) }),
          /* @__PURE__ */ (0, import_jsx_runtime67.jsxs)(Actions2, { children: [
            modeBtn("\u6A21\u5F0F\u540D", renameMode),
            modeBtn("\u80CC\u666F", () => void 0),
            modeBtn("BGM", () => void 0)
          ] }),
          /* @__PURE__ */ (0, import_jsx_runtime67.jsxs)(Actions2, { children: [
            modeBtn("\u706F\u5149", () => void 0),
            modeBtn("\u65F6\u95F4\u533A\u95F4", () => void 0),
            modeBtn("\u5220\u9664\u6A21\u5F0F", removeMode)
          ] })
        ] }) : null
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime67.jsxs)(Stack2, { gap: 1, style: { marginTop: "var(--w-space-4)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime67.jsx)("span", { style: { fontSize: "var(--w-size-small)", color: "var(--w-ink-body)" }, children: stateText }),
        /* @__PURE__ */ (0, import_jsx_runtime67.jsx)(Note2, { children: "\u70B9\u6761\u76EE\u6216\u6A21\u5F0F\u53F3\u4FA7\u7684\u300C\u5E94\u7528\u300D\u5373\u53EF\u7ACB\u523B\u5207\u6362\u5F53\u524D\u573A\u666F\uFF08\u70B9\u51FB\u5F0F\uFF0C\u65E0\u9700\u62D6\u62FD\uFF09\uFF1B\u5207\u6362\u7ED3\u679C\u4E0E\u6B64\u523B\u53EF\u7528\u6027\u4F1A\u663E\u793A\u5728\u4E0A\u65B9\u72B6\u6001\u680F\u3002\u65F6\u95F4\u5230\u70B9\u81EA\u52A8\u5207\u6362\uFF0CAI \u53EA\u80FD\u4ECE\u300C\u6B64\u523B\u53EF\u7528\u9009\u9879\u300D\u91CC\u9009\uFF08\u65E0\u6A21\u5F0F=\u4EC5\u6761\u76EE\uFF09\u3002\u6A21\u5F0F\u5199\u4E86\u65F6\u95F4\u533A\u95F4\u65F6\uFF0C\u53EA\u6709\u843D\u5728\u533A\u95F4\u5185\u624D\u80FD\u88AB AI \u5207\u6362\u3002" })
      ] })
    ] })
  ] });
}

// src/screens/story/showcase-edit.tsx
var import_react53 = __toESM(require_react(), 1);
var import_jsx_runtime68 = __toESM(require_jsx_runtime(), 1);
function ShowcaseEdit({ query, back }) {
  const [name, setName] = (0, import_react53.useState)(query.get("name") ?? "");
  const [steps] = (0, import_react53.useState)([]);
  const save = () => window.alert("\u5C55\u793A\u6848\u5DF2\u4FDD\u5B58");
  return /* @__PURE__ */ (0, import_jsx_runtime68.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(
      PageHeader,
      {
        title: "\u7F16\u8F91\u5C55\u793A\u6848",
        right: /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(TextBtn, { onClick: save, children: "\u4FDD\u5B58" })
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime68.jsx)("div", { className: "screen", style: { maxWidth: 760 }, children: /* @__PURE__ */ (0, import_jsx_runtime68.jsxs)(Stack2, { gap: 3, children: [
      /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(SectionTitle2, { children: "\u57FA\u672C\u4FE1\u606F" }),
      /* @__PURE__ */ (0, import_jsx_runtime68.jsxs)(Card, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(Row, { title: "\u540D\u79F0", right: /* @__PURE__ */ (0, import_jsx_runtime68.jsx)("div", { style: { width: 280 }, children: /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(Field, { label: "\u540D\u79F0", value: name, onChange: setName }) }) }),
        /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(Sep, {}),
        /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(
          Row,
          {
            title: "\u4E3B\u89D2",
            desc: "\uFF08\u672A\u9009\u62E9\uFF09",
            right: /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(TextBtn, { children: "\u9009\u62E9\u89D2\u8272" })
          }
        )
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(SectionTitle2, { children: "\u6B65\u9AA4\u5217\u8868" }),
      /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(Card, { children: /* @__PURE__ */ (0, import_jsx_runtime68.jsxs)("div", { style: { maxHeight: 260, overflowY: "auto" }, children: [
        steps.map((step) => /* @__PURE__ */ (0, import_jsx_runtime68.jsxs)("div", { children: [
          /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(Row, { title: step, right: /* @__PURE__ */ (0, import_jsx_runtime68.jsxs)(Actions2, { children: [
            /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(TextBtn, { children: "\u2191" }),
            /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(TextBtn, { children: "\u2193" }),
            /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(TextBtn, { children: "\xD7" })
          ] }) }),
          /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(Sep, {})
        ] }, step)),
        steps.length === 0 ? /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(Note2, { children: "\u6682\u65E0\u6B65\u9AA4\uFF0C\u70B9\u51FB\u4E0B\u65B9\u6309\u94AE\u6DFB\u52A0" }) : null
      ] }) }),
      /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(SectionTitle2, { children: "\u6DFB\u52A0\u6B65\u9AA4" }),
      /* @__PURE__ */ (0, import_jsx_runtime68.jsxs)(Card, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime68.jsxs)(Actions2, { children: [
          /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(TextBtn, { children: "\u6DFB\u52A0\u5BF9\u8BDD" }),
          /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(TextBtn, { children: "\u63D2\u5165CG" })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(Sep, {}),
        /* @__PURE__ */ (0, import_jsx_runtime68.jsxs)(Actions2, { children: [
          /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(TextBtn, { children: "\u6DFB\u52A0\u7ACB\u7ED8" }),
          /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(TextBtn, { children: "\u6E05\u7A7A\u7ACB\u7ED8" })
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(Sep, {}),
        /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(Actions2, { children: /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(TextBtn, { children: "\u5BFC\u5165\u4E34\u65F6\u7D20\u6750" }) })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime68.jsx)("div", { style: { display: "grid", gridTemplateColumns: "1fr" }, children: /* @__PURE__ */ (0, import_jsx_runtime68.jsx)(GlassButton, { size: "md", onClick: back, children: "\u8FD4\u56DE" }) })
    ] }) })
  ] });
}

// src/screens/story/showcase-list.tsx
var import_react54 = __toESM(require_react(), 1);
var import_jsx_runtime69 = __toESM(require_jsx_runtime(), 1);
function ShowcaseList({ navigate, back }) {
  const [scripts, setScripts] = (0, import_react54.useState)([]);
  const create = () => {
    const script = { id: `s${Date.now()}`, name: "\u6211\u7684\u7B2C\u4E00\u6BB5\u6F14\u51FA", characterName: "", steps: 0 };
    setScripts((list) => [script, ...list]);
    navigate(`/showcase-edit?name=${encodeURIComponent(script.name)}`);
  };
  const play = (script) => {
    if (script.steps === 0) {
      window.alert("\u8BE5\u5C55\u793A\u6848\u8FD8\u6CA1\u6709\u4EFB\u4F55\u6B65\u9AA4\uFF0C\u65E0\u6CD5\u64AD\u653E\u3002");
      return;
    }
    navigate("/showcase-play");
  };
  const remove = (script) => {
    if (!window.confirm(`\u786E\u5B9A\u5220\u9664\u300C${script.name}\u300D\uFF1F`)) return;
    setScripts((list) => list.filter((s) => s.id !== script.id));
  };
  return /* @__PURE__ */ (0, import_jsx_runtime69.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime69.jsx)(PageHeader, { title: "\u5F00\u53D1\u8005\u5C55\u793A\u6A21\u5F0F", onBack: back }),
    /* @__PURE__ */ (0, import_jsx_runtime69.jsxs)("div", { className: "screen", children: [
      /* @__PURE__ */ (0, import_jsx_runtime69.jsx)(Card, { children: scripts.map((script) => /* @__PURE__ */ (0, import_jsx_runtime69.jsxs)("div", { children: [
        /* @__PURE__ */ (0, import_jsx_runtime69.jsx)(
          Row,
          {
            title: script.name,
            desc: /* @__PURE__ */ (0, import_jsx_runtime69.jsxs)("span", { style: { display: "flex", flexDirection: "column", gap: 2 }, children: [
              /* @__PURE__ */ (0, import_jsx_runtime69.jsx)("span", { children: script.characterName }),
              /* @__PURE__ */ (0, import_jsx_runtime69.jsx)("span", { className: "cap", children: `\u6B65\u9AA4 ${script.steps}` })
            ] }),
            right: /* @__PURE__ */ (0, import_jsx_runtime69.jsxs)(Actions2, { children: [
              /* @__PURE__ */ (0, import_jsx_runtime69.jsx)(TextBtn, { onClick: () => navigate(`/showcase-edit?name=${encodeURIComponent(script.name)}`), children: "\u7F16\u8F91" }),
              /* @__PURE__ */ (0, import_jsx_runtime69.jsx)(TextBtn, { onClick: () => play(script), children: "\u25B6" }),
              /* @__PURE__ */ (0, import_jsx_runtime69.jsx)(TextBtn, { onClick: () => remove(script), children: "\xD7" })
            ] })
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime69.jsx)(Sep, {})
      ] }, script.id)) }),
      /* @__PURE__ */ (0, import_jsx_runtime69.jsx)(Stack2, { children: /* @__PURE__ */ (0, import_jsx_runtime69.jsx)("div", { style: { display: "grid", gridTemplateColumns: "1fr" }, children: /* @__PURE__ */ (0, import_jsx_runtime69.jsx)(GlassButton, { size: "md", onClick: create, children: "\uFF0B \u65B0\u5EFA\u5C55\u793A\u6848" }) }) })
    ] })
  ] });
}

// src/screens/story/showcase-play.tsx
var import_jsx_runtime70 = __toESM(require_jsx_runtime(), 1);
function ShowcasePlay({ back }) {
  const progressText = "";
  return /* @__PURE__ */ (0, import_jsx_runtime70.jsx)("div", { style: { position: "relative", height: "100%", overflow: "hidden" }, children: /* @__PURE__ */ (0, import_jsx_runtime70.jsx)(
    Glass,
    {
      as: "div",
      tone: "card",
      radius: 12,
      style: { position: "absolute", top: "var(--w-pad-card)", left: "var(--w-pad-card)", right: "var(--w-pad-card)" },
      children: /* @__PURE__ */ (0, import_jsx_runtime70.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "auto 1fr auto", alignItems: "center", gap: "var(--w-space-4)" }, children: [
        /* @__PURE__ */ (0, import_jsx_runtime70.jsx)("div", { children: /* @__PURE__ */ (0, import_jsx_runtime70.jsx)(GlassButton, { size: "sm", onClick: back, children: "\u25C0 \u9000\u51FA" }) }),
        /* @__PURE__ */ (0, import_jsx_runtime70.jsx)("span", { style: { textAlign: "center", fontSize: "var(--w-size-small)", color: "var(--w-ink-body)" }, children: progressText }),
        /* @__PURE__ */ (0, import_jsx_runtime70.jsx)("span", { className: "cap" })
      ] })
    }
  ) });
}

// src/screens/story/worldbook.tsx
var import_react55 = __toESM(require_react(), 1);
var import_jsx_runtime71 = __toESM(require_jsx_runtime(), 1);
function summaryOf2(entry) {
  const text = (entry.description.trim() ? entry.description : entry.idea).trim().replace(/\n/g, " ");
  return text.length <= 80 ? text : `${text.slice(0, 80)}\u2026`;
}
function Worldbook({ back }) {
  const [entries, setEntries] = (0, import_react55.useState)([]);
  const [draft, setDraft] = (0, import_react55.useState)(null);
  const [aiPrompt, setAiPrompt] = (0, import_react55.useState)("");
  const [mode, setMode] = (0, import_react55.useState)("\u7EAF\u6587\u5B57");
  const [status, setStatus] = (0, import_react55.useState)("");
  const [editStatus, setEditStatus] = (0, import_react55.useState)("");
  const openEditor = (entry, message = "") => {
    setDraft({ ...entry, characters: entry.characters.map((c) => ({ ...c })) });
    setEditStatus(message);
    setStatus("");
  };
  const newManual = () => openEditor(
    { id: `w${Date.now()}`, title: "\u672A\u547D\u540D\u4E16\u754C", description: "", idea: "", mode, characters: [] },
    "\u8FD8\u6CA1\u6709\u843D\u76D8\u3002\u5199\u597D\u6807\u9898\u4E0E\u8BBE\u5B9A\uFF0C\u6309\u300C\u4FDD\u5B58\u300D\u3002"
  );
  const saveEdit = () => {
    if (!draft) return;
    const title2 = draft.title.trim();
    if (title2.length === 0) {
      setEditStatus("\u6807\u9898\u4E0D\u80FD\u7A7A\u7740\u2014\u2014\u5B58\u6863\u5217\u8868\u8981\u9760\u5B83\u8BA4\u51FA\u8FD9\u4E00\u4EFD\u4E16\u754C\u3002");
      return;
    }
    const saved = {
      ...draft,
      title: title2,
      description: draft.description.trim(),
      idea: draft.idea.trim(),
      characters: draft.characters.filter((c) => c.name.trim().length > 0)
    };
    setEntries((list) => {
      const hit = list.findIndex((e) => e.id === saved.id);
      return hit < 0 ? [...list, saved] : list.map((e) => e.id === saved.id ? saved : e);
    });
    setDraft(null);
    setEditStatus("");
    setStatus("\u5DF2\u4FDD\u5B58\u3002\u7ED1\u5B9A\u5B83\u7684\u5B58\u6863\u4F1A\u5728\u4E0B\u4E00\u6B21\u6574\u7406\u8BB0\u5FC6\u65F6\u7528\u4E0A\u65B0\u8BBE\u5B9A\u3002");
  };
  const remove = (entry) => {
    const text = `\u300C${entry.title}\u300D\u4F1A\u4ECE\u4E16\u754C\u4E66\u5E93\u91CC\u5220\u6389\u3002\u5DF2\u7ECF\u7ED1\u5B9A\u5B83\u7684\u5176\u5B83\u5B58\u6863\u5C06\u5931\u53BB\u4E16\u754C\u8BBE\u5B9A\u3002`;
    if (!window.confirm(text)) return;
    setEntries((list) => list.filter((e) => e.id !== entry.id));
    setStatus("\u5DF2\u5220\u9664\u3002");
  };
  const setChar = (index, patch) => {
    if (!draft) return;
    setDraft({
      ...draft,
      characters: draft.characters.map((c, i) => i === index ? { ...c, ...patch } : c)
    });
  };
  return /* @__PURE__ */ (0, import_jsx_runtime71.jsxs)(PageShell, { children: [
    /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(
      PageHeader,
      {
        title: "\u4E16\u754C\u4E66",
        subtitle: status || void 0,
        onBack: draft ? () => setDraft(null) : back,
        right: draft ? null : /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(TextBtn, { onClick: newManual, children: "\u65B0\u5EFA" })
      }
    ),
    /* @__PURE__ */ (0, import_jsx_runtime71.jsx)("div", { className: "screen", style: { maxWidth: 960 }, children: !draft ? /* @__PURE__ */ (0, import_jsx_runtime71.jsxs)(Stack2, { gap: 4, children: [
      /* @__PURE__ */ (0, import_jsx_runtime71.jsxs)(Stack2, { gap: 1, children: [
        /* @__PURE__ */ (0, import_jsx_runtime71.jsx)("span", { className: "eyebrow", children: "WORLDS" }),
        /* @__PURE__ */ (0, import_jsx_runtime71.jsx)("p", { style: { margin: 0, fontSize: "var(--w-size-small)", color: "var(--w-ink-body)" }, children: "\u5B58\u6863\u7ED1\u5B9A\u7684\u5C31\u662F\u8FD9\u91CC\u7684\u4E00\u4EFD\u3002\u53EF\u4EE5\u81EA\u5DF1\u5199\uFF0C\u4E5F\u53EF\u4EE5\u8BA9 AI \u9020\u4E00\u4E2A\uFF0C\u518D\u7531\u4F60\u6539\u3002" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime71.jsxs)(Card, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(SectionTitle2, { children: "\u8BA9 AI \u9020\u4E00\u4E2A\u4E16\u754C" }),
        /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(
          Field,
          {
            multiline: true,
            label: "\u8BA9 AI \u9020\u4E00\u4E2A\u4E16\u754C",
            value: aiPrompt,
            onChange: setAiPrompt,
            placeholder: "\u4E00\u53E5\u8BDD\u5C31\u591F\uFF1A\u6218\u540E\u5E9F\u571F\u4E0A\u7684\u79FB\u52A8\u65C5\u9986\uFF1B\u6216\u8005\u4E00\u5EA7\u53EA\u5728\u96E8\u591C\u51FA\u73B0\u7684\u65E7\u57CE\u3002"
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime71.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(
            Select,
            {
              label: "\u751F\u6210\u6A21\u5F0F",
              value: mode,
              options: [
                { value: "\u7EAF\u6587\u5B57", label: "\u7EAF\u6587\u5B57" },
                { value: "\u5E26\u7ACB\u7ED8", label: "\u5E26\u7ACB\u7ED8" }
              ],
              onChange: setMode
            }
          ),
          /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(SignalButton, { onClick: () => {
            if (!aiPrompt.trim()) setStatus("\u5148\u5199\u4E00\u53E5\u4F60\u60F3\u8981\u7684\u4E16\u754C\u3002");
          }, children: "\u751F\u6210\u5E76\u7F16\u8F91" })
        ] })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime71.jsxs)(Card, { children: [
        entries.map((entry) => {
          const bits = [entry.characters.length > 0 ? `${entry.characters.length} \u4F4D\u89D2\u8272` : "\u6CA1\u6709\u89D2\u8272", entry.mode];
          return /* @__PURE__ */ (0, import_jsx_runtime71.jsxs)("div", { children: [
            /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(
              Row,
              {
                title: entry.title || "\u672A\u547D\u540D\u4E16\u754C",
                desc: /* @__PURE__ */ (0, import_jsx_runtime71.jsxs)("span", { style: { display: "flex", flexDirection: "column", gap: 3 }, children: [
                  summaryOf2(entry) ? /* @__PURE__ */ (0, import_jsx_runtime71.jsx)("span", { children: summaryOf2(entry) }) : null,
                  /* @__PURE__ */ (0, import_jsx_runtime71.jsx)("span", { className: "cap", children: bits.join(" \xB7 ") })
                ] }),
                right: /* @__PURE__ */ (0, import_jsx_runtime71.jsxs)(Actions2, { children: [
                  /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(TextBtn, { onClick: () => openEditor(entry), children: "\u7F16\u8F91" }),
                  /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(TextBtn, { onClick: () => remove(entry), children: "\u5220\u9664" })
                ] })
              }
            ),
            /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(Sep, {})
          ] }, entry.id);
        }),
        entries.length === 0 ? /* @__PURE__ */ (0, import_jsx_runtime71.jsxs)(Stack2, { gap: 1, children: [
          /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(SectionTitle2, { children: "\u8FD8\u6CA1\u6709\u4E16\u754C\u4E66" }),
          /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(Note2, { children: "\u4E0A\u9762\u5199\u4E00\u53E5\uFF0C\u8BA9 AI \u9020\u7B2C\u4E00\u4E2A\uFF1B\u6216\u8005\u70B9\u53F3\u4E0A\u89D2\u300C\u65B0\u5EFA\u300D\u81EA\u5DF1\u5199\u3002" })
        ] }) : null
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(Note2, { children: "\u6CA1\u7ED1\u5B9A\u4E16\u754C\u4E66\u7684\u5B58\u6863\u662F\u666E\u901A\u9152\u9986\uFF1A\u53EA\u804A\u5929\uFF0C\u5730\u56FE\u9501\u4E0A\u3002\u7ED1\u5B9A\u4E4B\u540E\u968F\u65F6\u53EF\u4EE5\u6362\uFF0C\u4E5F\u53EF\u4EE5\u89E3\u7ED1\u3002" })
    ] }) : /* @__PURE__ */ (0, import_jsx_runtime71.jsxs)(Stack2, { gap: 4, children: [
      /* @__PURE__ */ (0, import_jsx_runtime71.jsxs)(Stack2, { gap: 1, children: [
        /* @__PURE__ */ (0, import_jsx_runtime71.jsx)("span", { className: "eyebrow", children: "EDIT WORLD" }),
        /* @__PURE__ */ (0, import_jsx_runtime71.jsx)("p", { style: { margin: 0, fontSize: "var(--w-size-small)", color: "var(--w-ink-body)" }, children: "\u6539\u54EA\u4E00\u6761\u90FD\u884C\u3002\u4FDD\u5B58\u4E4B\u540E\uFF0C\u7ED1\u5B9A\u5B83\u7684\u5B58\u6863\u5728\u4E0B\u4E00\u6B21\u6574\u7406\u8BB0\u5FC6\u65F6\u751F\u6548\u3002" })
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime71.jsxs)(Card, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime71.jsx)("span", { className: "cap", children: "\u6807\u9898" }),
        /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(Field, { label: "\u6807\u9898", value: draft.title, onChange: (title2) => setDraft({ ...draft, title: title2 }), placeholder: "\u7ED9\u8FD9\u4E2A\u4E16\u754C\u4E00\u4E2A\u540D\u5B57" }),
        /* @__PURE__ */ (0, import_jsx_runtime71.jsx)("span", { className: "cap", children: "\u4E16\u754C\u8BBE\u5B9A" }),
        /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(
          Field,
          {
            multiline: true,
            label: "\u4E16\u754C\u8BBE\u5B9A",
            value: draft.description,
            onChange: (description) => setDraft({ ...draft, description }),
            placeholder: "\u8FD9\u4E2A\u4E16\u754C\u4E00\u76F4\u6210\u7ACB\u7684\u4E8B\uFF1A\u5730\u7406\u3001\u89C4\u5219\u3001\u6C14\u6C1B\u3001\u52BF\u529B\u3001\u65E5\u5E38\u3002\u4E0D\u8981\u5199\u5177\u4F53\u5267\u60C5\u3002"
          }
        ),
        /* @__PURE__ */ (0, import_jsx_runtime71.jsx)("span", { className: "cap", children: "\u6700\u521D\u7684\u60F3\u6CD5" }),
        /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(
          Field,
          {
            multiline: true,
            label: "\u6700\u521D\u7684\u60F3\u6CD5",
            value: draft.idea,
            onChange: (idea) => setDraft({ ...draft, idea }),
            placeholder: "\u4F60\u5F53\u521D\u7528\u6765\u751F\u6210\u5B83\u7684\u90A3\u53E5\u8BDD\u3002\u7559\u7740\u81EA\u5DF1\u770B\uFF0C\u4E5F\u65B9\u4FBF\u4EE5\u540E\u8BA9 AI \u91CD\u5199\u3002"
          }
        )
      ] }),
      /* @__PURE__ */ (0, import_jsx_runtime71.jsxs)(Stack2, { gap: 2, children: [
        /* @__PURE__ */ (0, import_jsx_runtime71.jsxs)("div", { style: { display: "flex", alignItems: "center", justifyContent: "space-between", gap: "var(--w-space-3)" }, children: [
          /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(SectionTitle2, { children: "\u4E16\u754C\u91CC\u7684\u4EBA" }),
          /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(
            TextBtn,
            {
              onClick: () => setDraft({
                ...draft,
                characters: [...draft.characters, { name: "", gender: "\u5973", personality: "", description: "" }]
              }),
              children: "\u6DFB\u52A0\u89D2\u8272"
            }
          )
        ] }),
        /* @__PURE__ */ (0, import_jsx_runtime71.jsxs)(Card, { children: [
          draft.characters.map((c, index) => /* @__PURE__ */ (0, import_jsx_runtime71.jsxs)("div", { children: [
            /* @__PURE__ */ (0, import_jsx_runtime71.jsxs)(Stack2, { gap: 2, children: [
              /* @__PURE__ */ (0, import_jsx_runtime71.jsxs)("div", { style: { display: "grid", gridTemplateColumns: "1fr 76px", gap: "var(--w-space-3)" }, children: [
                /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(Field, { label: "\u89D2\u8272\u540D", value: c.name, onChange: (name) => setChar(index, { name }), placeholder: "\u89D2\u8272\u540D" }),
                /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(Field, { label: "\u6027\u522B", value: c.gender, onChange: (gender) => setChar(index, { gender }), placeholder: "\u6027\u522B" })
              ] }),
              /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(
                Field,
                {
                  label: "\u6027\u683C",
                  value: c.personality,
                  onChange: (personality) => setChar(index, { personality }),
                  placeholder: "\u6027\u683C\uFF08\u4E00\u4E24\u53E5\u8BDD\uFF09"
                }
              ),
              /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(
                Field,
                {
                  multiline: true,
                  label: "\u63CF\u8FF0",
                  value: c.description,
                  onChange: (description) => setChar(index, { description }),
                  placeholder: "\u5916\u89C2\u3001\u80CC\u666F\u3001\u5728\u8FD9\u4E2A\u4E16\u754C\u91CC\u7684\u4F4D\u7F6E"
                }
              ),
              /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(Actions2, { children: /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(
                TextBtn,
                {
                  onClick: () => setDraft({ ...draft, characters: draft.characters.filter((_, i) => i !== index) }),
                  children: "\u79FB\u9664\u8FD9\u4E2A\u89D2\u8272"
                }
              ) })
            ] }),
            /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(Sep, {})
          ] }, index)),
          draft.characters.length === 0 ? /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(Note2, { children: "\u8FD9\u4EFD\u4E16\u754C\u8FD8\u6CA1\u6709\u89D2\u8272\u3002\u53EF\u4EE5\u53EA\u5199\u4E16\u754C\uFF0C\u4E5F\u53EF\u4EE5\u5728\u8FD9\u91CC\u52A0\u51E0\u4E2A\u4EBA\u3002" }) : null
        ] })
      ] }),
      editStatus ? /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(Note2, { warn: true, children: editStatus }) : null,
      /* @__PURE__ */ (0, import_jsx_runtime71.jsxs)(Actions2, { children: [
        /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(SignalButton, { onClick: saveEdit, children: "\u4FDD\u5B58" }),
        /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(TextBtn, { children: "AI \u6DA6\u8272" }),
        /* @__PURE__ */ (0, import_jsx_runtime71.jsx)("span", { style: { flex: 1 } }),
        /* @__PURE__ */ (0, import_jsx_runtime71.jsx)(TextBtn, { onClick: () => setDraft(null), children: "\u53D6\u6D88\u7F16\u8F91" })
      ] })
    ] }) })
  ] });
}

// src/screens/story/index.tsx
var screens9 = {
  newrun: Newrun,
  worldbook: Worldbook,
  chatimport: Chatimport,
  scenelibrary: Scenelibrary,
  SceneCalibrationPage,
  forcecast: Forcecast,
  "showcase-list": ShowcaseList,
  "showcase-edit": ShowcaseEdit,
  "showcase-play": ShowcasePlay,
  novelselect: NovelSelect,
  novelworld: Novelworld
};

// src/screens/index.ts
var SCREENS = {
  ...screens4,
  ...screens6,
  ...screens7,
  ...screens8,
  ...screens2,
  ...screens,
  ...screens9,
  ...screens3,
  ...screens5
};

// .ssr-smoke.tsx
var import_jsx_runtime72 = __toESM(require_jsx_runtime(), 1);
var bridge = {
  request: async () => {
    throw new Error("ssr: no backend");
  },
  onEvent: () => () => void 0
};
var rows = [];
var bad = 0;
for (const route2 of ROUTES) {
  if (route2.id === "title" || route2.id === "settings" || route2.id === "appearance") continue;
  const Screen = SCREENS[route2.id];
  if (!Screen) {
    rows.push(`MISSING ${route2.path}`);
    bad++;
    continue;
  }
  try {
    const html = renderToStaticMarkup(
      /* @__PURE__ */ (0, import_jsx_runtime72.jsx)(Screen, { bridge, path: route2.path, query: new URLSearchParams(), navigate: () => void 0, back: () => void 0 })
    );
    const title2 = /data-page-title[^>]*>([^<]*)/.exec(html)?.[1] ?? "(no title)";
    const textLen = html.replace(/<[^>]+>/g, "").trim().length;
    const flag = textLen < 80 ? "THIN" : "ok";
    if (flag !== "ok") bad++;
    rows.push(`${flag.padEnd(5)} ${route2.path.padEnd(24)} len=${String(textLen).padEnd(5)} title=${title2}`);
  } catch (error) {
    bad++;
    rows.push(`FAIL  ${route2.path} -> ${error.message}`);
  }
}
console.log(rows.join("\n"));
console.log(`
${ROUTES.length - 3} routes, ${bad} problems`);
/*! Bundled license information:

react/cjs/react.production.js:
  (**
   * @license React
   * react.production.js
   *
   * Copyright (c) Meta Platforms, Inc. and affiliates.
   *
   * This source code is licensed under the MIT license found in the
   * LICENSE file in the root directory of this source tree.
   *)

react/cjs/react.development.js:
  (**
   * @license React
   * react.development.js
   *
   * Copyright (c) Meta Platforms, Inc. and affiliates.
   *
   * This source code is licensed under the MIT license found in the
   * LICENSE file in the root directory of this source tree.
   *)

react/cjs/react-jsx-runtime.production.js:
  (**
   * @license React
   * react-jsx-runtime.production.js
   *
   * Copyright (c) Meta Platforms, Inc. and affiliates.
   *
   * This source code is licensed under the MIT license found in the
   * LICENSE file in the root directory of this source tree.
   *)

react/cjs/react-jsx-runtime.development.js:
  (**
   * @license React
   * react-jsx-runtime.development.js
   *
   * Copyright (c) Meta Platforms, Inc. and affiliates.
   *
   * This source code is licensed under the MIT license found in the
   * LICENSE file in the root directory of this source tree.
   *)
*/
