using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KeyMapper.Classes {
	public struct HashCode {
		private readonly int hashCode;

		private HashCode(int hashCode) {
			this.hashCode = hashCode;
		}

		public static HashCode Start => new HashCode(17);

		public static implicit operator int(HashCode hashCode) {
			return hashCode.GetHashCode();
		}

		public HashCode Hash<T>(T obj) {
			var hash = !Equals(obj, default(T)) ? obj.GetHashCode() : 0;
			return new HashCode(Hash(hash));
		}

		public HashCode Hash<T, K>(T obj, Func<T, K> expression) {
			int hash = 0;
			if (Equals(obj, default(T)) == false) {
				K value = expression.Invoke(obj);

				if (Equals(value, default(K)) == false) {
					hash = expression.Invoke(obj).GetHashCode();
				}
			}

			return new HashCode(Hash(hash));
		}

		public override int GetHashCode() {
			return hashCode;
		}

		private int Hash(int newHashCode) {
			unchecked {
				return newHashCode + hashCode * 31; // 31 because it is a shift and subtract on the CPU
			}
		}
	}
}
