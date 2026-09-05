import { shallowEqual, useDispatch, useSelector } from "react-redux"
import type { AppDispatch, RootState } from "./store"

export const useAppDispatch = () => useDispatch<AppDispatch>()

export function useAppSelector<T>(
  selector: (state: RootState) => T,
  equalityFn?: (a: T, b: T) => boolean,
): T {
  return useSelector(selector, equalityFn ?? shallowEqual)
}
